using TinyTrophy.Infrastructure.Watchers;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure;

/// <summary>
/// Composite service that coordinates all registered <see cref="IGameWatcher"/> instances.
/// Adding a new source only requires implementing <see cref="IGameWatcher"/> (or extending
/// <see cref="GameWatcherBase"/>) and registering the instance at the composition root.
/// </summary>
public interface IGameWatcherService : IDisposable
{
	/// <summary>
	/// Fired when any watcher detects a new achievement unlock.
	/// </summary>
	event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;

	/// <summary>
	/// Fired when any watcher detects changes in a game's achievement set.
	/// </summary>
	event EventHandler<GameChangedEventArgs>? AchievementsChanged;

	void Start();
	void Stop();

	/// <summary>
	/// Restarts all watchers and returns the total number of games added and removed. When
	/// <paramref name="reportChanges"/> is false, those games are not refreshed individually.
	/// </summary>
	(int Added, int Removed) Restart(bool reportChanges = true);
}

public sealed class GameWatcherService(IEnumerable<IGameWatcher> watchers)
	: IGameWatcherService
{
	private readonly IGameWatcher[] _watchers = [.. watchers];
	private readonly Lock _restartLock = new();
	private bool _subscribed;

	public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
	public event EventHandler<GameChangedEventArgs>? AchievementsChanged;

	public void Start() => Start(reportChanges: true);

	private void Start(bool reportChanges)
	{
		EnsureSubscriptions();
		foreach (IGameWatcher watcher in _watchers)
			watcher.Start(reportChanges);
	}

	public void Stop()
	{
		foreach (IGameWatcher watcher in _watchers)
			watcher.Stop();
	}

	public (int Added, int Removed) Restart(bool reportChanges = true)
	{
		// Restarts run on background threads, so serialize them to avoid interleaving
		lock (_restartLock)
		{
			Stop();
			Start(reportChanges);
			return (
				_watchers.Sum(w => w.LastRestartChanges.Added),
				_watchers.Sum(w => w.LastRestartChanges.Removed));
		}
	}

	public void Dispose()
	{
		foreach (IGameWatcher watcher in _watchers)
			watcher.Dispose();
	}

	private void EnsureSubscriptions()
	{
		if (_subscribed)
			return;
		_subscribed = true;

		foreach (IGameWatcher watcher in _watchers)
		{
			watcher.AchievementUnlocked += (s, e) => AchievementUnlocked?.Invoke(s, e);
			watcher.AchievementsChanged += (s, e) => AchievementsChanged?.Invoke(s, e);
		}
	}
}
