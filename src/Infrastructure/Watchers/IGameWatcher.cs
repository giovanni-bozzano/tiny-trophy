using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure.Watchers;

/// <summary>
/// Monitors file-system changes for a specific achievement source and detects newly unlocked achievements.
/// Implementations are registered in the composition root and managed by <see cref="GameWatcherService"/>.
/// </summary>
public interface IGameWatcher : IDisposable
{
	/// <summary>
	/// Fired when a new achievement unlock is detected.
	/// </summary>
	event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;

	/// <summary>
	/// Fired when the set of achievements for a game changes (added or removed).
	/// </summary>
	event EventHandler<GameChangedEventArgs>? AchievementsChanged;

	/// <summary>
	/// Starts watching for achievement changes.
	/// </summary>
	void Start();

	/// <summary>
	/// Number of games that appeared and disappeared during the last restart.
	/// </summary>
	(int Added, int Removed) LastRestartChanges { get; }

	/// <summary>
	/// Stops watching and releases file-system resources.
	/// </summary>
	void Stop();
}
