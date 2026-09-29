namespace TinyTrophy.Models;

/// <summary>
/// Event args raised when the achievement set of a single game changes on disk.
/// </summary>
public sealed class GameChangedEventArgs(
	AchievementSource source,
	string gameId)
	: EventArgs
{
	public AchievementSource Source { get; } = source;
	public string GameId { get; } = gameId;
}
