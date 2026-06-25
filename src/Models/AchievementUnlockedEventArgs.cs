namespace TinyTrophy.Models;

/// <summary>
/// Event args for achievement unlock notifications.
/// </summary>
public sealed class AchievementUnlockedEventArgs(
	string appId,
	Achievement achievement)
	: EventArgs
{
	public string AppId { get; } = appId;
	public Achievement Achievement { get; } = achievement;
}
