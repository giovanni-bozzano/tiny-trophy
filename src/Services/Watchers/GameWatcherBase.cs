using System.Collections.Concurrent;
using TinyTrophy.Models;

namespace TinyTrophy.Services.Watchers;

/// <summary>
/// Base class for game watchers providing shared debouncing and known-state diffing infrastructure.
/// Subclasses implement <see cref="InitializeKnownState"/>, <see cref="SetupFileWatchers"/>,
/// and <see cref="DetectNewAchievementsAsync"/> for their specific source.
/// </summary>
public abstract class GameWatcherBase : IGameWatcher
{
	private readonly List<FileSystemWatcher> _watchers = [];
	private readonly ConcurrentDictionary<string, HashSet<string>> _knownUnlocks = new();
	private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTimers = new();
	private readonly ConcurrentDictionary<string, SemaphoreSlim> _processingLocks = new();

	protected const int DebounceMs = 800;

	public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
	public event EventHandler<string>? AchievementsChanged;

	public void Start()
	{
		Stop();
		InitializeKnownState();
		SetupFileWatchers();
	}

	public void Stop()
	{
		foreach (FileSystemWatcher watcher in _watchers)
		{
			watcher.EnableRaisingEvents = false;
			watcher.Dispose();
		}
		_watchers.Clear();

		foreach (CancellationTokenSource cts in _debounceTimers.Values)
			cts.Cancel();
		_debounceTimers.Clear();
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
		HashSet<string> currentIds = currentUnlocked
			.Select(a => a.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		HashSet<string> previousIds = _knownUnlocks.GetOrAdd(key, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

		List<Achievement> newAchievements = [.. currentUnlocked.Where(a => !previousIds.Contains(a.Id))];
		bool hasRemovals = previousIds.Count > 0 && !previousIds.IsSubsetOf(currentIds);

		_knownUnlocks[key] = currentIds;

		if (newAchievements.Count == 0 && !hasRemovals)
			return [];

		AchievementsChanged?.Invoke(this, key);

		return newAchievements;
	}

	/// <summary>
	/// Fires the <see cref="AchievementUnlocked"/> event for a single achievement.
	/// </summary>
	protected void RaiseAchievementUnlocked(
		string gameKey,
		Achievement achievement)
	{
		AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs(gameKey, achievement));
	}

	/// <summary>
	/// Schedules debounced detection for the given key.
	/// Multiple rapid file events for the same key collapse into one detection run.
	/// </summary>
	protected void ScheduleDetection(string key)
	{
		if (_debounceTimers.TryRemove(key, out CancellationTokenSource? previousCts))
			previousCts.Cancel();

		CancellationTokenSource cts = new();
		_debounceTimers[key] = cts;

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(DebounceMs, cts.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			_debounceTimers.TryRemove(key, out _);

			SemaphoreSlim semaphore = _processingLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
			await semaphore.WaitAsync();
			try
			{
				await DetectNewAchievementsAsync(key);
			}
			finally
			{
				semaphore.Release();
			}
		});
	}
}
