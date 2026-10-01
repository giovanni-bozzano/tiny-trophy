using System.Collections.Concurrent;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure.Watchers;

/// <summary>
/// Base class for game watchers providing shared debouncing and known-state diffing infrastructure.
/// Subclasses implement <see cref="InitializeKnownState"/>, <see cref="SetupFileWatchers"/>,
/// and <see cref="DetectNewAchievementsAsync"/> for their specific source.
/// </summary>
public abstract class GameWatcherBase : IGameWatcher
{
	private readonly List<FileSystemWatcher> _watchers = [];
	private readonly ConcurrentDictionary<string, HashSet<string>> _knownUnlocks = new();
	private readonly HashSet<string> _knownGames = new(StringComparer.OrdinalIgnoreCase);
	private bool _hasStarted;
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTimers = new();

	// Per-game locks with the number of detections using or waiting for them, removed once unused
	private readonly Dictionary<string, (SemaphoreSlim Semaphore, int Users)> _processingLocks = [];
	private readonly Lock _processingLocksGate = new();

	// Bumped by Stop(); detection runs from an older generation must not touch state or raise events
	private int _generation;
	private static readonly AsyncLocal<int> RunGeneration = new();

	protected const int DebounceMs = 800;

	public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
	public event EventHandler<GameChangedEventArgs>? AchievementsChanged;

	/// <summary>
	/// The achievement source this watcher monitors.
	/// </summary>
	protected abstract AchievementSource Source { get; }

	public void Start()
	{
		Stop();

		HashSet<string> previousGames = new(_knownGames, StringComparer.OrdinalIgnoreCase);
		_knownGames.Clear();
		_knownUnlocks.Clear();

		InitializeKnownState();
		SetupFileWatchers();

		// On a restart, report games that appeared or disappeared so they are refreshed individually
		LastRestartChanges = (0, 0);
		if (_hasStarted)
		{
			List<string> added = [.. _knownGames.Where(k => !previousGames.Contains(k))];
			List<string> removed = [.. previousGames.Where(k => !_knownGames.Contains(k))];
			LastRestartChanges = (added.Count, removed.Count);

			foreach (string key in added.Concat(removed))
				AchievementsChanged?.Invoke(this, new GameChangedEventArgs(Source, key));
		}
		_hasStarted = true;
	}

	public (int Added, int Removed) LastRestartChanges { get; private set; }

	public void Stop()
	{
		Interlocked.Increment(ref _generation);

		foreach (FileSystemWatcher watcher in _watchers)
		{
			watcher.EnableRaisingEvents = false;
			watcher.Dispose();
		}
		_watchers.Clear();

		foreach (CancellationTokenSource cts in _debounceTimers.Values)
			TryCancel(cts);
		_debounceTimers.Clear();
	}

	/// <summary>
	/// Cancels a debounce token, which its detection run may have already finished and disposed.
	/// </summary>
	private static void TryCancel(CancellationTokenSource cts)
	{
		try
		{
			cts.Cancel();
		}
		catch (ObjectDisposedException) { }
	}

	public void Dispose()
	{
		Stop();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Populates initial known unlock state so only new unlocks trigger events.
	/// </summary>
	protected abstract void InitializeKnownState();

	/// <summary>
	/// Creates and registers file-system watchers for this source.
	/// Use <see cref="AddWatcher"/> to register each watcher.
	/// </summary>
	protected abstract void SetupFileWatchers();

	/// <summary>
	/// Detects new achievements for the given key and fires events for any new unlocks.
	/// </summary>
	protected abstract Task DetectNewAchievementsAsync(string key);

	/// <summary>
	/// Registers a file-system watcher for lifecycle management.
	/// </summary>
	protected void AddWatcher(FileSystemWatcher watcher)
	{
		_watchers.Add(watcher);
	}

	/// <summary>
	/// Seeds the known unlock state for a given game key.
	/// </summary>
	protected void SetKnownUnlocks(
		string key,
		HashSet<string> unlockedIds)
	{
		_knownGames.Add(key);
		if (unlockedIds.Count > 0)
			_knownUnlocks[key] = unlockedIds;
	}

	/// <summary>
	/// Compares current unlocks against known state, updates state, and fires events for new unlocks.
	/// Returns the list of newly unlocked achievements.
	/// </summary>
	protected List<Achievement> DiffAndUpdate(
		string key,
		List<Achievement> currentUnlocked)
	{
		if (IsStaleRun())
			return [];

		HashSet<string> currentIds = currentUnlocked
			.Select(a => a.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		HashSet<string> previousIds = _knownUnlocks.GetOrAdd(key, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

		List<Achievement> newAchievements = [.. currentUnlocked.Where(a => !previousIds.Contains(a.Id))];
		bool hasRemovals = previousIds.Count > 0 && !previousIds.IsSubsetOf(currentIds);

		_knownUnlocks[key] = currentIds;

		if (newAchievements.Count == 0 && !hasRemovals)
			return [];

		AchievementsChanged?.Invoke(this, new GameChangedEventArgs(Source, key));

		return newAchievements;
	}

	/// <summary>
	/// Fires the <see cref="AchievementUnlocked"/> event for a single achievement.
	/// </summary>
	protected void RaiseAchievementUnlocked(
		string gameKey,
		Achievement achievement)
	{
		if (IsStaleRun())
			return;

		AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs(gameKey, achievement));
	}

	/// <summary>
	/// True when called from a detection run scheduled before the latest <see cref="Stop"/>.
	/// </summary>
	private bool IsStaleRun() => RunGeneration.Value != Volatile.Read(ref _generation);

	/// <summary>
	/// Schedules debounced detection for the given key.
	/// Multiple rapid file events for the same key collapse into one detection run.
	/// </summary>
	protected void ScheduleDetection(string key)
	{
		if (_debounceTimers.TryRemove(key, out CancellationTokenSource? previousCts))
			TryCancel(previousCts);

		CancellationTokenSource cts = new();
		_debounceTimers[key] = cts;
		int generation = Volatile.Read(ref _generation);

		_ = Task.Run(async () =>
		{
			RunGeneration.Value = generation;

			// The detection run owns its token: whoever replaces or stops it only cancels it
			using (cts)
			{
				try
				{
					await Task.Delay(DebounceMs, cts.Token);
				}
				catch (OperationCanceledException)
				{
					return;
				}

				// Only remove this run's own entry, not a newer one scheduled meanwhile
				_debounceTimers.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, cts));
			}

			SemaphoreSlim semaphore = AcquireProcessingLock(key);
			await semaphore.WaitAsync();
			try
			{
				if (!IsStaleRun())
					await DetectNewAchievementsAsync(key);
			}
			finally
			{
				semaphore.Release();
				ReleaseProcessingLock(key);
			}
		});
	}

	private SemaphoreSlim AcquireProcessingLock(string key)
	{
		lock (_processingLocksGate)
		{
			if (!_processingLocks.TryGetValue(key, out (SemaphoreSlim Semaphore, int Users) entry))
				entry = (new SemaphoreSlim(1, 1), 0);

			(SemaphoreSlim semaphore, int users) = entry;

			_processingLocks[key] = (semaphore, users + 1);
			return semaphore;
		}
	}

	private void ReleaseProcessingLock(string key)
	{
		lock (_processingLocksGate)
		{
			(SemaphoreSlim semaphore, int users) = _processingLocks[key];
			if (users > 1)
			{
				_processingLocks[key] = (semaphore, users - 1);
				return;
			}

			_processingLocks.Remove(key);
			semaphore.Dispose();
		}
	}
}
