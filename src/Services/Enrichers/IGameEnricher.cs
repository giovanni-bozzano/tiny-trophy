using TinyTrophy.Models;

namespace TinyTrophy.Services.Enrichers;

/// <summary>
/// Enriches games with metadata (names, icons, descriptions, global stats) after scanning.
/// Each enricher declares the sources it owns via <see cref="Sources"/>.
/// A source has at most one enricher. Sources that produce complete data from the scanner
/// (e.g. ShadPS4) don't need an enricher.
/// </summary>
public interface IGameEnricher
{
	IReadOnlyCollection<AchievementSource> Sources { get; }
	Task EnrichAsync(Game game, CancellationToken ct = default);
}
