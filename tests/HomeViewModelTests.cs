using TinyTrophy.Infrastructure;
using TinyTrophy.Models;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Tests;

public sealed class HomeViewModelTests
{
	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		for (int i = 0; i < 300 && !condition(); i++)
			await Task.Delay(10);

		Assert.True(condition());
	}

	[Fact]
	public async Task ReloadGamesAsync_WhenNoGamesAreFound_ShowsTheEmptyState()
	{
		MainViewModel main = new(
			new FakeAchievementService(),
			new FakeSettingsService(),
			new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();

		Assert.True(main.HomeViewModel.IsEmpty);
		Assert.Equal(0, main.HomeViewModel.TotalGameCount);
		Assert.False(main.HomeViewModel.IsLoading);
		Assert.True(main.HomeViewModel.IsListVisible);
	}

	[Fact]
	public async Task ReloadGamesAsync_PopulatesTheListAndTheProfile()
	{
		Game game = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements =
			[
				new Achievement { Id = "A", Name = "A", IsUnlocked = true },
				new Achievement { Id = "B", Name = "B" }
			]
		};

		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([game]));
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();

		Assert.False(main.HomeViewModel.IsEmpty);
		Assert.Single(main.HomeViewModel.Games);
		Assert.Equal(1, main.HomeViewModel.UserProfile.TotalGames);
		Assert.Equal(1, main.HomeViewModel.UserProfile.TotalAchievements);
	}

	[Fact]
	public async Task ReloadGamesAsync_WhileOneIsRunning_QueuesASecondRun()
	{
		TaskCompletionSource<IReadOnlyList<Game>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeAchievementService achievements = new(() => gate.Task);
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		Task firstLoad = main.HomeViewModel.ReloadGamesAsync();
		await WaitUntilAsync(() => achievements.ScanCalls == 1);

		// A reload asked for while the first one runs is queued instead of being dropped
		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Equal(1, achievements.ScanCalls);

		gate.SetResult([]);
		await firstLoad;

		Assert.Equal(2, achievements.ScanCalls);
		Assert.False(main.HomeViewModel.IsLoading);
	}

	[Fact]
	public async Task ReloadGamesAsync_StartsTheLocalScanBeforeKeyValidationFinishes()
	{
		TaskCompletionSource<ApiKeyValidationResult> validation = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeSteamApiService steamApi = new() { Validation = () => validation.Task };
		FakeAchievementService achievements = new();
		MainViewModel main = new(
			achievements,
			new FakeSettingsService { Settings = new AppSettings { SteamApiKey = "KEY" } },
			steamApi);

		Task loading = main.HomeViewModel.ReloadGamesAsync();
		await WaitUntilAsync(() => achievements.ScanCalls == 1);

		// The scan has finished while the key check is still pending, so local data never waited for Steam
		Assert.False(loading.IsCompleted);
		Assert.Equal(1, steamApi.ValidationCalls);

		validation.SetResult(ApiKeyValidationResult.Valid);
		await loading;

		Assert.False(main.HomeViewModel.HasLoadError);
		Assert.True(main.HomeViewModel.IsEmpty);
	}

	[Fact]
	public async Task ReloadGamesAsync_WhenTheScanFails_ShowsTheError()
	{
		FakeAchievementService achievements = new(() => throw new InvalidOperationException("scanner exploded"));
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();

		Assert.True(main.HomeViewModel.HasLoadError);
		Assert.Contains("scanner exploded", main.HomeViewModel.LoadError);
		Assert.False(main.HomeViewModel.IsEmpty);
		Assert.False(main.HomeViewModel.IsLoading);
	}

	[Fact]
	public async Task RefreshGameAsync_ReplacesTheChangedGame()
	{
		Game initial = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements = [new Achievement { Id = "A", IsUnlocked = true }, new Achievement { Id = "B" }]
		};
		Game updated = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements =
			[
				new Achievement { Id = "A", IsUnlocked = true },
				new Achievement { Id = "B", IsUnlocked = true },
				new Achievement { Id = "C" }
			]
		};

		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => [updated]
		};
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Equal(2, main.HomeViewModel.Games.Single().TotalCount);

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.Equal(3, main.HomeViewModel.Games.Single().TotalCount);
	}

	[Fact]
	public async Task LoadGamesCommand_ReloadsTheList()
	{
		FakeAchievementService achievements = new();
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.LoadGamesCommand.ExecuteAsync(null);

		Assert.Equal(1, achievements.ScanCalls);
		Assert.True(main.HomeViewModel.IsEmpty);
	}

	[Fact]
	public async Task ReloadGamesAsync_WhileEnriching_HidesTheListAndReportsNoStats()
	{
		Game game = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements = [new Achievement { Id = "A", IsUnlocked = true }]
		};
		TaskCompletionSource<bool> enrichment = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([game]))
		{
			Enrichment = () => enrichment.Task
		};
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		Task loading = main.HomeViewModel.ReloadGamesAsync();
		await WaitUntilAsync(() => main.HomeViewModel.IsEnriching);

		Assert.False(main.HomeViewModel.IsListVisible);
		Assert.True(main.IsBusy);
		Assert.Equal("N/A", main.HomeViewModel.StatsGames);

		enrichment.SetResult(true);
		await loading;

		Assert.True(main.HomeViewModel.IsListVisible);
		Assert.False(main.IsBusy);
		Assert.Equal("1", main.HomeViewModel.StatsGames);
	}

	[Fact]
	public async Task SortAlphabetical_ReordersAndPersistsTheSetting()
	{
		AppSettings settings = new();
		Game beta = new() { AppId = "1", Name = "Beta", Achievements = [new Achievement { Id = "A", IsUnlocked = true }] };
		Game alpha = new() { AppId = "2", Name = "Alpha", Achievements = [new Achievement { Id = "A", IsUnlocked = true }] };
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([beta, alpha]));
		MainViewModel main = new(achievements, new FakeSettingsService { Settings = settings }, new FakeSteamApiService());
		await main.HomeViewModel.ReloadGamesAsync();

		main.HomeViewModel.SortAlphabeticalCommand.Execute(null);

		Assert.Equal(new[] { "Alpha", "Beta" }, main.HomeViewModel.Games.Select(g => g.Name));
		Assert.Equal("A-Z \u2193", main.HomeViewModel.AlphabeticalSortLabel);
		Assert.Equal("Alphabetical", settings.HomeSort.Mode);

		main.HomeViewModel.SortAlphabeticalCommand.Execute(null);

		Assert.Equal(new[] { "Beta", "Alpha" }, main.HomeViewModel.Games.Select(g => g.Name));
		Assert.Equal("A-Z \u2191", main.HomeViewModel.AlphabeticalSortLabel);
		Assert.False(settings.HomeSort.Ascending);
	}

	[Fact]
	public async Task SortByRecent_OrdersByTheLatestUnlockAndPersistsTheSetting()
	{
		AppSettings settings = new()
		{
			HomeSort = new SortSettings { Mode = "Alphabetical", Ascending = true },
			Achievements = new AchievementsSettings { HideZeroPercent = false, MergeDuplicate = true }
		};
		Game never = new() { AppId = "1", Name = "A", Achievements = [new Achievement { Id = "A" }] };
		Game older = new()
		{
			AppId = "2",
			Name = "B",
			Achievements = [new Achievement { Id = "A", IsUnlocked = true, UnlockTime = new DateTime(2023, 1, 1) }]
		};
		Game newer = new()
		{
			AppId = "3",
			Name = "C",
			Achievements = [new Achievement { Id = "A", IsUnlocked = true, UnlockTime = new DateTime(2024, 1, 1) }]
		};
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([never, older, newer]));
		MainViewModel main = new(achievements, new FakeSettingsService { Settings = settings }, new FakeSteamApiService());
		await main.HomeViewModel.ReloadGamesAsync();

		main.HomeViewModel.SortByRecentCommand.Execute(null);

		Assert.Equal(new[] { "C", "B", "A" }, main.HomeViewModel.Games.Select(g => g.Name));
		Assert.Equal("Time \u2193", main.HomeViewModel.TimeSortLabel);
		Assert.Equal("Recent", settings.HomeSort.Mode);

		main.HomeViewModel.SortByRecentCommand.Execute(null);

		Assert.Equal(new[] { "A", "B", "C" }, main.HomeViewModel.Games.Select(g => g.Name));
		Assert.Equal("Time \u2191", main.HomeViewModel.TimeSortLabel);
	}

	[Fact]
	public void GameItemViewModel_FormatsCompletion()
	{
		GameItemViewModel item = new(new Game
		{
			Name = "Game",
			Achievements =
			[
				new Achievement { Id = "A", IsUnlocked = true },
				new Achievement { Id = "B" },
				new Achievement { Id = "C" },
				new Achievement { Id = "D" }
			]
		});

		Assert.Equal("1/4", item.CompletionText);
		Assert.Equal("25%", item.PercentText);
	}
}
