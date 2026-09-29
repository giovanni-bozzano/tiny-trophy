using TinyTrophy.Infrastructure.Enrichers;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure;

/// <summary>
/// Aggregates achievements from all configured parsers and merges duplicates.
/// </summary>
public interface IAchievementService
{
	/// <summary>
	/// Scans every source and returns one entry per source and game, not merged.
	/// </summary>
	Task<IReadOnlyList<Game>> ScanGamesAsync(IProgress<(string scannerName, int current, int total)>? progress = null, CancellationToken ct = default);

	/// <summary>
	/// Re-reads and enriches a single game of a single source. Returns <see langword="null"/> if the source
	/// can't refresh individual games, in which case a full scan is required.
	/// </summary>
	Task<IReadOnlyList<Game>?> ScanGameAsync(AchievementSource source, string gameId, CancellationToken ct = default);

	Task EnrichGamesAsync(IReadOnlyList<Game> games, IProgress<int>? progress = null, CancellationToken ct = default);

	/// <summary>
	/// Combines entries of the same game coming from different sources, according to the settings.
	/// The input games are left untouched, so they can be merged again later.
	/// </summary>
	IReadOnlyList<Game> MergeGames(IReadOnlyList<Game> games);

	UserProfile GetUserProfile(IReadOnlyList<Game> games);
}

public sealed class AchievementService(
	IEnumerable<IAchievementScanner> scanners,
	IEnumerable<IGameEnricher> enrichers,
	ISettingsService settings)
	: IAchievementService
{
	public async Task<IReadOnlyList<Game>> ScanGamesAsync(
		IProgress<(string scannerName, int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		List<Game> allGames = [];

		foreach (IAchievementScanner scanner in scanners)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				Progress<(int current, int total)>? relay = progress is not null
					? new(p => progress.Report((scanner.DisplayName, p.current, p.total)))
					: null;

				IReadOnlyList<Game> games = await scanner.ParseAsync(relay, ct);
				allGames.AddRange(games);
			}
			catch
			{
				// Ignore scanners that fail
			}
		}

		return allGames;
	}

	public async Task<IReadOnlyList<Game>?> ScanGameAsync(
		AchievementSource source,
		string gameId,
		CancellationToken ct = default)
	{
		if (scanners.OfType<ISingleGameScanner>().FirstOrDefault(s => s.Source == source) is not ISingleGameScanner scanner)
			return null;

		IReadOnlyList<Game> games = await scanner.ParseGameAsync(gameId, ct);
		await EnrichGamesAsync(games, ct: ct);
		return games;
	}

	public async Task EnrichGamesAsync(
		IReadOnlyList<Game> games,
		IProgress<int>? progress = null,
		CancellationToken ct = default)
	{
		int total = games.Count;
		if (total == 0)
			return;

		int completed = 0;
		using SemaphoreSlim semaphore = new(3);

		IEnumerable<Task> tasks = games.Select(async game =>
		{
			await semaphore.WaitAsync(ct);
			try
			{
				ct.ThrowIfCancellationRequested();

				IGameEnricher? enricher = FindEnricher(game.Source);
				if (enricher is not null)
					await enricher.EnrichAsync(game, ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch { }
			finally
			{
				semaphore.Release();
				int done = Interlocked.Increment(ref completed);
				progress?.Report((int)((double)done / total * 100));
			}
		});

		await Task.WhenAll(tasks);
	}

	private readonly Dictionary<AchievementSource, IGameEnricher> _enricherMap =
		enrichers.SelectMany(e => e.Sources.Select(s => (s, e))).ToDictionary(x => x.s, x => x.e);

	private IGameEnricher? FindEnricher(AchievementSource source) =>
		_enricherMap.GetValueOrDefault(source);

	public UserProfile GetUserProfile(IReadOnlyList<Game> games)
	{
		List<Achievement> allAchievements = [.. games.SelectMany(g => g.Achievements).Where(a => a.IsUnlocked)];
		int totalGames = games.Count;
		int totalAchievements = allAchievements.Count;
		double overallCompletion = games.Count > 0 ? games.Average(g => g.CompletionPercentage) : 0;

		return new UserProfile
		{
			TotalAchievements = totalAchievements,
			TotalGames = totalGames,
			PerfectGames = games.Count(g => g.TotalCount > 0 && g.CompletionPercentage >= 100),
			OverallCompletion = Math.Round(overallCompletion, 1)
		};
	}

	public IReadOnlyList<Game> MergeGames(IReadOnlyList<Game> games)
	{
		if (!settings.Settings.Achievements.MergeDuplicate)
			return games;

		IEnumerable<IGrouping<string, Game>> grouped = games.GroupBy(g => g.AppId);
		List<Game> merged = [];

		foreach (IGrouping<string, Game> group in grouped)
		{
			if (!group.Skip(1).Any())
			{
				merged.Add(group.First());
				continue;
			}

			// Copy the primary entry, since merging changes its achievements
			Game primary = Copy(group.First());

			foreach (Game? other in group.Skip(1))
			{
				foreach (Achievement ach in other.Achievements)
				{
					Achievement? existing = primary.Achievements.FirstOrDefault(a => a.Id == ach.Id);
					if (existing is null)
					{
						primary.Achievements.Add(Copy(ach));
					}
					else if (!existing.IsUnlocked && ach.IsUnlocked)
					{
						existing.IsUnlocked = true;
						existing.UnlockTime = ach.UnlockTime;
					}
				}
			}

			merged.Add(primary);
		}

		return merged;
	}

	private static Game Copy(Game game) => new()
	{
		AppId = game.AppId,
		Name = game.Name,
		ImageUri = game.ImageUri,
		Source = game.Source,
		FolderPath = game.FolderPath,
		Achievements = [.. game.Achievements.Select(Copy)],
		Playtime = game.Playtime,
		LastPlayed = game.LastPlayed
	};

	private static Achievement Copy(Achievement achievement) => new()
	{
		Id = achievement.Id,
		Name = achievement.Name,
		Description = achievement.Description,
		IconUri = achievement.IconUri,
		IconLockedUri = achievement.IconLockedUri,
		IsUnlocked = achievement.IsUnlocked,
		UnlockTime = achievement.UnlockTime,
		IsHidden = achievement.IsHidden,
		GlobalPercentage = achievement.GlobalPercentage,
		TrophyType = achievement.TrophyType
	};
}
