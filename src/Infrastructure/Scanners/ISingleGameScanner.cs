using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure.Scanners;

/// <summary>
/// A scanner that can re-read a single game from disk, so a file change doesn't require a full rescan.
/// </summary>
public interface ISingleGameScanner : IAchievementScanner
{
	/// <summary>
	/// Parses every entry of the given game for this source. Returns an empty list if the game no longer has achievement data.
	/// </summary>
	Task<IReadOnlyList<Game>> ParseGameAsync(string gameId, CancellationToken ct = default);
}
