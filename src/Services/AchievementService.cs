using TinyTrophy.Models;
using TinyTrophy.Services.Enrichers;
using TinyTrophy.Services.Scanners;

namespace TinyTrophy.Services;

/// <summary>
/// Aggregates achievements from all configured parsers and merges duplicates.
/// </summary>
public interface IAchievementService
{
	Task<IReadOnlyList<Game>> ScanGamesAsync(IProgress<(string scannerName, int current, int total)>? progress = null, CancellationToken ct = default);
	Task EnrichGamesAsync(IReadOnlyList<Game> games, IProgress<int>? progress = null, CancellationToken ct = default);
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

		return MergeGames(allGames);
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

	private List<Game> MergeGames(List<Game> games)
	{
		if (!settings.Settings.Achievements.MergeDuplicate)
			return games;

		IEnumerable<IGrouping<string, Game>> grouped = games.GroupBy(g => g.AppId);
		List<Game> merged = [];

		foreach (IGrouping<string, Game> group in grouped)
		{
			Game primary = group.First();

			foreach (Game? other in group.Skip(1))
			{
				foreach (Achievement ach in other.Achievements)
				{
					Achievement? existing = primary.Achievements.FirstOrDefault(a => a.Id == ach.Id);
					if (existing is null)
					{
						primary.Achievements.Add(ach);
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
}
