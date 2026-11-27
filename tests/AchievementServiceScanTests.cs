using System.Collections.Concurrent;
using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Enrichers;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class AchievementServiceScanTests
{
	private sealed class FakeScanner
		: ISingleGameScanner
	{
		public AchievementSource Source { get; init; } = AchievementSource.SteamEmulator;

		public string DisplayName => "Fake";

		public List<string> RequestedGameIds { get; } = [];

		public IReadOnlyList<Game> Games { get; init; } = [];

		public Task<IReadOnlyList<Game>> ParseAsync(
			IProgress<(int current, int total)>? progress = null,
			CancellationToken ct = default) => Task.FromResult(Games);

		public Task<IReadOnlyList<Game>> ParseGameAsync(
			string gameId,
			CancellationToken ct = default)
		{
			RequestedGameIds.Add(gameId);
			return Task.FromResult(Games);
		}
	}

	private sealed class ThrowingScanner
		: IAchievementScanner
	{
		public AchievementSource Source => AchievementSource.SteamOfficial;

		public string DisplayName => "Throwing";

		public Task<IReadOnlyList<Game>> ParseAsync(
			IProgress<(int current, int total)>? progress = null,
			CancellationToken ct = default) =>
			throw new InvalidOperationException("scanner failure");
	}

	private sealed class FakeEnricher
		: IGameEnricher
	{
		public int Calls;
		public bool Throws { get; init; }

		public IReadOnlyCollection<AchievementSource> Sources { get; } =
			[AchievementSource.SteamEmulator, AchievementSource.SteamOfficial];

		public Task EnrichAsync(
			Game game,
			CancellationToken ct = default)
		{
			Interlocked.Increment(ref Calls);

			if (Throws)
				throw new InvalidOperationException("enricher failure");

			game.Name = $"Enriched {game.Name}";
			return Task.CompletedTask;
		}
	}

	private sealed class RecordingProgress
		: IProgress<int>
	{
		public ConcurrentBag<int> Values { get; } = [];

		public void Report(int value) => Values.Add(value);
	}

	private static AchievementService CreateService(
		IEnumerable<IAchievementScanner> scanners,
		IEnumerable<IGameEnricher> enrichers) =>
		new(scanners, enrichers, new FakeSettingsService());

	private static Game CreateGame(string appId = "480") => new()
	{
		AppId = appId,
		Name = "Game",
		Achievements = [new Achievement { Id = "A", IsUnlocked = true }]
	};

	[Fact]
	public async Task ScanGameAsync_WithoutASingleGameScanner_ReturnsNull()
	{
		AchievementService service = CreateService([], []);

		Assert.Null(await service.ScanGameAsync(AchievementSource.SteamEmulator, "480"));
	}

	[Fact]
	public async Task ScanGameAsync_ForAnotherSource_ReturnsNull()
	{
		FakeScanner scanner = new() { Source = AchievementSource.SteamEmulator, Games = [CreateGame()] };
		AchievementService service = CreateService([scanner], []);

		Assert.Null(await service.ScanGameAsync(AchievementSource.ShadPs4, "480"));
		Assert.Empty(scanner.RequestedGameIds);
	}

	[Fact]
	public async Task ScanGameAsync_ParsesTheRequestedGameAndEnrichesIt()
	{
		FakeScanner scanner = new() { Games = [CreateGame()] };
		FakeEnricher enricher = new();
		AchievementService service = CreateService([scanner], [enricher]);

		IReadOnlyList<Game>? games = await service.ScanGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.NotNull(games);
		Game game = Assert.Single(games!);
		Assert.Equal("Enriched Game", game.Name);
		Assert.Equal(new[] { "480" }, scanner.RequestedGameIds);
		Assert.Equal(1, enricher.Calls);
	}

	[Fact]
	public async Task ScanGamesAsync_IgnoresAScannerThatFails()
	{
		FakeScanner scanner = new() { Games = [CreateGame()] };
		AchievementService service = CreateService([new ThrowingScanner(), scanner], []);

		IReadOnlyList<Game> games = await service.ScanGamesAsync();

		Assert.Single(games);
	}

	[Fact]
	public async Task EnrichGamesAsync_IgnoresAnEnricherThatFails()
	{
		FakeEnricher enricher = new() { Throws = true };
		AchievementService service = CreateService([], [enricher]);

		await service.EnrichGamesAsync([CreateGame(), CreateGame("481")]);

		Assert.Equal(2, enricher.Calls);
	}

	[Fact]
	public async Task EnrichGamesAsync_ReportsProgressUpToCompletion()
	{
		RecordingProgress progress = new();
		AchievementService service = CreateService([], [new FakeEnricher()]);

		await service.EnrichGamesAsync([CreateGame("1"), CreateGame("2"), CreateGame("3")], progress);

		Assert.Equal(3, progress.Values.Count);
		Assert.Contains(100, progress.Values);
	}

	[Fact]
	public async Task EnrichGamesAsync_HonorsCancellation()
	{
		using CancellationTokenSource cts = new();
		cts.Cancel();
		FakeEnricher enricher = new();
		AchievementService service = CreateService([], [enricher]);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => service.EnrichGamesAsync([CreateGame()], ct: cts.Token));

		Assert.Equal(0, enricher.Calls);
	}

	[Fact]
	public async Task EnrichGamesAsync_WithoutGames_DoesNotReport()
	{
		RecordingProgress progress = new();
		AchievementService service = CreateService([], [new FakeEnricher()]);

		await service.EnrichGamesAsync([], progress);

		Assert.Empty(progress.Values);
	}
}
