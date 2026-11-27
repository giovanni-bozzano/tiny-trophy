using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

/// <summary>
/// A throwaway directory that is deleted when the test finishes.
/// </summary>
internal sealed class TempDirectory
	: IDisposable
{
	public string Root { get; }

	public TempDirectory()
	{
		Root = Path.Combine(Path.GetTempPath(), "tinytrophy-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
	}

	public string CreateFile(
		string relativePath,
		string contents)
	{
		string fullPath = Path.Combine(Root, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
		File.WriteAllText(fullPath, contents);
		return fullPath;
	}

	public string CreateFile(
		string relativePath,
		byte[] contents)
	{
		string fullPath = Path.Combine(Root, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
		File.WriteAllBytes(fullPath, contents);
		return fullPath;
	}

	public string CreateDirectory(string relativePath)
	{
		string fullPath = Path.Combine(Root, relativePath);
		Directory.CreateDirectory(fullPath);
		return fullPath;
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch
		{
			// Best effort: a leftover temp directory must never fail a test
		}
	}
}

internal sealed class FakeSettingsService
	: ISettingsService
{
	public AppSettings Settings { get; set; } = new();

	public int SaveCalls;
	public Exception? SaveError { get; set; }

	public Task LoadAsync() => Task.CompletedTask;

	public Task SaveAsync()
	{
		Interlocked.Increment(ref SaveCalls);
		if (SaveError is not null)
			throw SaveError;
		return Task.CompletedTask;
	}
}

internal sealed class FakeSteamApiService
	: ISteamApiService
{
	public event EventHandler<ApiKeyValidationResult>? ApiKeyValidated;

	public ApiKeyValidationResult Result { get; set; } = ApiKeyValidationResult.Valid;

	/// <summary>
	/// Lets a test hold validation open and observe what starts while it is pending.
	/// </summary>
	public Func<Task<ApiKeyValidationResult>>? Validation { get; set; }

	public int ValidationCalls;

	public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(
		string apiKey,
		CancellationToken ct = default)
	{
		Interlocked.Increment(ref ValidationCalls);
		ApiKeyValidationResult result = Validation is null ? Result : await Validation();
		ApiKeyValidated?.Invoke(this, result);
		return result;
	}

	public Task<SteamGameMetadata?> GetSteamGameMetadataAsync(
		string appId,
		CancellationToken ct = default) => Task.FromResult<SteamGameMetadata?>(null);

	public Task<Dictionary<string, double>> GetGlobalAchievementStatsAsync(
		string appId,
		CancellationToken ct = default) => Task.FromResult(new Dictionary<string, double>());

	public List<string> OwnedGames { get; set; } = [];

	public Dictionary<string, List<Achievement>> PlayerAchievements { get; } = new(StringComparer.Ordinal);

	public int OwnedGamesCalls;
	public int PlayerAchievementsCalls;

	public Task<List<string>> GetOwnedGamesAsync(
		string steamId,
		CancellationToken ct = default)
	{
		Interlocked.Increment(ref OwnedGamesCalls);
		return Task.FromResult(new List<string>(OwnedGames));
	}

	public Task<List<Achievement>> GetPlayerAchievementsAsync(
		string steamId,
		string appId,
		CancellationToken ct = default)
	{
		Interlocked.Increment(ref PlayerAchievementsCalls);
		return Task.FromResult(
			PlayerAchievements.TryGetValue(appId, out List<Achievement>? achievements)
				? new List<Achievement>(achievements)
				: new List<Achievement>());
	}

	public int ClearCacheCalls;
	public int ResetOfflineCalls;

	public void ClearCache() => Interlocked.Increment(ref ClearCacheCalls);

	public void ResetOfflineState() => Interlocked.Increment(ref ResetOfflineCalls);
}

internal sealed class FakeAchievementService
	: IAchievementService
{
	private readonly Func<Task<IReadOnlyList<Game>>> _games;

	public int ScanCalls;
	public int SingleGameCalls;

	public Func<string, IReadOnlyList<Game>?>? SingleGame { get; set; }

	/// <summary>
	/// Replaces the default identity merge, so a test can exercise the real merging rules.
	/// </summary>
	public Func<IReadOnlyList<Game>, IReadOnlyList<Game>>? Merge { get; set; }

	/// <summary>
	/// Lets a test hold enrichment open and observe the states around it.
	/// </summary>
	public Func<Task>? Enrichment { get; set; }

	public FakeAchievementService(Func<Task<IReadOnlyList<Game>>>? games = null) =>
		_games = games ?? (() => Task.FromResult<IReadOnlyList<Game>>([]));

	public Task<IReadOnlyList<Game>> ScanGamesAsync(
		IProgress<(string scannerName, int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		Interlocked.Increment(ref ScanCalls);
		return _games();
	}

	public Task<IReadOnlyList<Game>?> ScanGameAsync(
		AchievementSource source,
		string gameId,
		CancellationToken ct = default)
	{
		Interlocked.Increment(ref SingleGameCalls);
		return Task.FromResult(SingleGame?.Invoke(gameId));
	}

	public Task EnrichGamesAsync(
		IReadOnlyList<Game> games,
		IProgress<int>? progress = null,
		CancellationToken ct = default) => Enrichment?.Invoke() ?? Task.CompletedTask;

	public IReadOnlyList<Game> MergeGames(IReadOnlyList<Game> games) => Merge?.Invoke(games) ?? games;

	public UserProfile GetUserProfile(IReadOnlyList<Game> games) => new()
	{
		TotalGames = games.Count,
		TotalAchievements = games.Sum(g => g.UnlockedCount),
		PerfectGames = games.Count(g => g.TotalCount > 0 && g.CompletionPercentage >= 100),
		OverallCompletion = games.Count > 0 ? games.Average(g => g.CompletionPercentage) : 0
	};
}
