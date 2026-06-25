using TinyTrophy.Models;

namespace TinyTrophy.Services.Enrichers;

/// <summary>
/// Enriches Steam games (emulator and official) with metadata from the Steam Web API:
/// display names, descriptions, icons, hidden flags, and global unlock percentages.
/// </summary>
public sealed class SteamGameEnricher(ISteamApiService steamApi)
	: IGameEnricher
{
	/// <inheritdoc/>
	public IReadOnlyCollection<AchievementSource> Sources { get; } =
		[AchievementSource.SteamEmulator, AchievementSource.SteamOfficial];

	public async Task EnrichAsync(
		Game game,
		CancellationToken ct = default)
	{
		SteamGameMetadata? metadata = await steamApi.GetSteamGameMetadataAsync(game.AppId, ct);
		if (metadata is null)
			return;

		if (!string.IsNullOrEmpty(metadata.Name))
			game.Name = metadata.Name;
		if (!string.IsNullOrEmpty(metadata.HeaderImageUri))
			game.ImageUri = metadata.HeaderImageUri;

		// Apply metadata to existing achievements
		HashSet<string> knownIds = new(game.Achievements.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
		Dictionary<string, SteamAchievementSchema> schemaLookup = new(metadata.Achievements, StringComparer.OrdinalIgnoreCase);
		int matchCount = 0;

		foreach (Achievement ach in game.Achievements)
		{
			if (schemaLookup.TryGetValue(ach.Id, out SteamAchievementSchema? schema))
			{
				matchCount++;
				if (!string.IsNullOrEmpty(schema.Name))
					ach.Name = schema.Name;
				if (!string.IsNullOrEmpty(schema.Description))
					ach.Description = schema.Description;
				ach.IconUri = schema.IconUri;
				ach.IconLockedUri = schema.IconLockedUri;
				ach.IsHidden = schema.IsHidden;
				ach.GlobalPercentage = schema.GlobalPercentage;
			}
		}

		// Only add locked achievements from the schema if local IDs actually match.
		// Some emulators use numeric IDs ("0", "1", "2") that don't correspond to schema names.
		if (matchCount > 0)
		{
			foreach ((string achId, SteamAchievementSchema schema) in metadata.Achievements)
			{
				if (knownIds.Contains(achId))
					continue;

				game.Achievements.Add(new Achievement
				{
					Id = achId,
					Name = schema.Name,
					Description = schema.Description,
					IconUri = schema.IconUri,
					IconLockedUri = schema.IconLockedUri,
					IsHidden = schema.IsHidden,
					GlobalPercentage = schema.GlobalPercentage
				});
			}

			// Remove locked achievements not found in the schema (stray emulator entries).
			// Unlocked ones are kept to preserve user progress.
			game.Achievements.RemoveAll(a => !a.IsUnlocked && !schemaLookup.ContainsKey(a.Id));
		}
	}
}
