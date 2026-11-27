using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class SteamOfficialScannerTests
{
	private sealed class RecordingProgress
		: IProgress<(int current, int total)>
	{
		private readonly Lock _gate = new();
		private readonly List<(int current, int total)> _reports = [];

		public List<(int current, int total)> Reports
		{
			get
			{
				lock (_gate)
					return [.. _reports];
			}
		}

		public void Report((int current, int total) value)
		{
			lock (_gate)
				_reports.Add(value);
		}
	}

	private static AppSettings SettingsWithCredentials() => new()
	{
		SteamApiKey = "KEY",
		SteamId = "76561198000000000"
	};

	private static Achievement Achievement(string id) => new() { Id = id, Name = id, IsUnlocked = true };

	[Fact]
	public async Task ParseAsync_WhenTheSourceIsDisabled_ReturnsNothing()
	{
		AppSettings settings = SettingsWithCredentials();
		settings.SteamOfficialEnabled = false;
		FakeSettingsService settingsService = new() { Settings = settings };
		FakeSteamApiService steamApi = new() { OwnedGames = ["480"] };
		SteamOfficialScanner scanner = new(settingsService, steamApi);

		IReadOnlyList<Game> games = await scanner.ParseAsync();

		Assert.Empty(games);
		Assert.Equal(0, steamApi.OwnedGamesCalls);
	}

	[Fact]
	public async Task ParseAsync_WithoutAnApiKey_ReturnsNothing()
	{
		AppSettings settings = SettingsWithCredentials();
		settings.SteamApiKey = "  ";
		FakeSteamApiService steamApi = new() { OwnedGames = ["480"] };
		SteamOfficialScanner scanner = new(new FakeSettingsService { Settings = settings }, steamApi);

		Assert.Empty(await scanner.ParseAsync());
		Assert.Equal(0, steamApi.OwnedGamesCalls);
	}

	[Fact]
	public async Task ParseAsync_WithoutASteamId_ReturnsNothing()
	{
		AppSettings settings = SettingsWithCredentials();
		settings.SteamId = string.Empty;
		FakeSteamApiService steamApi = new() { OwnedGames = ["480"] };
		SteamOfficialScanner scanner = new(new FakeSettingsService { Settings = settings }, steamApi);

		Assert.Empty(await scanner.ParseAsync());
		Assert.Equal(0, steamApi.OwnedGamesCalls);
	}

	[Fact]
	public async Task ParseAsync_WithoutOwnedGames_ReturnsNothing()
	{
		FakeSteamApiService steamApi = new();
		SteamOfficialScanner scanner = new(
			new FakeSettingsService { Settings = SettingsWithCredentials() },
			steamApi);

		Assert.Empty(await scanner.ParseAsync());
		Assert.Equal(1, steamApi.OwnedGamesCalls);
		Assert.Equal(0, steamApi.PlayerAchievementsCalls);
	}

	[Fact]
	public async Task ParseAsync_SkipsOwnedGamesWithoutAchievements()
	{
		FakeSteamApiService steamApi = new() { OwnedGames = ["480", "730"] };
		steamApi.PlayerAchievements["480"] = [Achievement("A")];
		SteamOfficialScanner scanner = new(
			new FakeSettingsService { Settings = SettingsWithCredentials() },
			steamApi);

		Game game = Assert.Single(await scanner.ParseAsync());

		Assert.Equal("480", game.AppId);
		Assert.Equal(AchievementSource.SteamOfficial, game.Source);
		Assert.Equal("AppID: 480", game.Name);
		Assert.Single(game.Achievements);
		Assert.Equal(2, steamApi.PlayerAchievementsCalls);
	}

	[Fact]
	public async Task ParseAsync_ReportsProgressForEveryOwnedGame()
	{
		FakeSteamApiService steamApi = new() { OwnedGames = ["1", "2", "3"] };
		steamApi.PlayerAchievements["2"] = [Achievement("A")];
		RecordingProgress progress = new();
		SteamOfficialScanner scanner = new(
			new FakeSettingsService { Settings = SettingsWithCredentials() },
			steamApi);

		IReadOnlyList<Game> games = await scanner.ParseAsync(progress);

		Assert.Single(games);
		Assert.Equal(3, progress.Reports.Count);
		Assert.All(progress.Reports, report => Assert.Equal(3, report.total));
		Assert.Equal(new[] { 1, 2, 3 }, progress.Reports.Select(r => r.current).Order());
	}

	[Fact]
	public async Task ParseAsync_UsesTheConfiguredSteamId()
	{
		FakeSettingsService settingsService = new() { Settings = SettingsWithCredentials() };
		RecordingSteamApiService steamApi = new();
		SteamOfficialScanner scanner = new(settingsService, steamApi);

		await scanner.ParseAsync();

		Assert.Equal(settingsService.Settings.SteamId, steamApi.LastSteamId);
	}

	private sealed class RecordingSteamApiService
		: ISteamApiService
	{
		public event EventHandler<ApiKeyValidationResult>? ApiKeyValidated;

		public string? LastSteamId { get; private set; }

		public Task<ApiKeyValidationResult> ValidateApiKeyAsync(
			string apiKey,
			CancellationToken ct = default)
		{
			ApiKeyValidated?.Invoke(this, ApiKeyValidationResult.Valid);
			return Task.FromResult(ApiKeyValidationResult.Valid);
		}

		public Task<SteamGameMetadata?> GetSteamGameMetadataAsync(
			string appId,
			CancellationToken ct = default) => Task.FromResult<SteamGameMetadata?>(null);

		public Task<Dictionary<string, double>> GetGlobalAchievementStatsAsync(
			string appId,
			CancellationToken ct = default) => Task.FromResult(new Dictionary<string, double>());

		public Task<List<string>> GetOwnedGamesAsync(
			string steamId,
			CancellationToken ct = default)
		{
			LastSteamId = steamId;
			return Task.FromResult(new List<string>());
		}

		public Task<List<Achievement>> GetPlayerAchievementsAsync(
			string steamId,
			string appId,
			CancellationToken ct = default) => Task.FromResult(new List<Achievement>());

		public void ClearCache() { }

		public void ResetOfflineState() { }
	}
}
