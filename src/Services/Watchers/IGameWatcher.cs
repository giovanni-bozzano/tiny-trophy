using TinyTrophy.Models;

namespace TinyTrophy.Services.Watchers;

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
	/// The event arg is the game key (e.g. appId or npwrId).
	/// </summary>
	event EventHandler<string>? AchievementsChanged;

	/// <summary>
	/// Starts watching for achievement changes.
	/// </summary>
	void Start();

	/// <summary>
	/// Stops watching and releases file-system resources.
	/// </summary>
	void Stop();
}
