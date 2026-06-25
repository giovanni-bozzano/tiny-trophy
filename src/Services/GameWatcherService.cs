using TinyTrophy.Models;
using TinyTrophy.Services.Watchers;

namespace TinyTrophy.Services;

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
	event EventHandler<string>? AchievementsChanged;

	void Start();
	void Stop();
	void Restart();
}

public sealed class GameWatcherService(IEnumerable<IGameWatcher> watchers)
	: IGameWatcherService
{
	private readonly IGameWatcher[] _watchers = [.. watchers];
	private bool _subscribed;

	public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
	public event EventHandler<string>? AchievementsChanged;

	public void Start()
	{
		EnsureSubscriptions();
		foreach (IGameWatcher watcher in _watchers)
			watcher.Start();
	}

	public void Stop()
	{
		foreach (IGameWatcher watcher in _watchers)
			watcher.Stop();
	}

	public void Restart()
	{
		Stop();
		Start();
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
