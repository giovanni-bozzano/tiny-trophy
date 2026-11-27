using TinyTrophy.Infrastructure;
using TinyTrophy.Models;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Tests;

public sealed class HomeViewModelRefreshTests
{
	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		for (int i = 0; i < 300 && !condition(); i++)
			await Task.Delay(10);

		Assert.True(condition());
	}

	private static Achievement Unlocked(string id) => new() { Id = id, Name = id, IsUnlocked = true };

	private static Achievement Locked(string id) => new() { Id = id, Name = id };

	[Fact]
	public async Task RefreshGameAsync_WhenTheGameDisappears_RemovesItFromTheList()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A")] };
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => []
		};
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Single(main.HomeViewModel.Games);

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.Empty(main.HomeViewModel.Games);
		Assert.Equal(0, main.HomeViewModel.TotalGameCount);
		Assert.True(main.HomeViewModel.IsEmpty);
	}

	[Fact]
	public async Task RefreshGameAsync_WhenANewGameAppears_AddsIt()
	{
		Game newcomer = new() { AppId = "730", Name = "Counter-Strike 2", Achievements = [Unlocked("A")] };
		FakeAchievementService achievements = new();
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Empty(main.HomeViewModel.Games);

		achievements.SingleGame = _ => [newcomer];
		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "730");

		Assert.Single(main.HomeViewModel.Games);
		Assert.Equal(1, main.HomeViewModel.TotalGameCount);
		Assert.False(main.HomeViewModel.IsEmpty);
	}

	[Fact]
	public async Task RefreshGameAsync_WhenASingleGameRefreshIsUnavailable_ReloadsEverything()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A")] };
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]));
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Equal(1, achievements.ScanCalls);

		// SingleGame is null, so the source reports that it cannot refresh one game
		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.Equal(2, achievements.ScanCalls);
		Assert.Single(main.HomeViewModel.Games);
	}

	[Fact]
	public async Task RefreshGameAsync_WhenTheScanThrows_FallsBackToAFullReload()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A")] };
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => throw new IOException("achievement file vanished")
		};
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Equal(1, achievements.ScanCalls);

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.Equal(2, achievements.ScanCalls);
		Assert.Single(main.HomeViewModel.Games);
	}

	[Fact]
	public async Task RefreshGameAsync_DuringAFullLoad_IsQueuedAndAppliedAfterwards()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A"), Locked("B")] };
		Game refreshed = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements = [Unlocked("A"), Unlocked("B"), Locked("C")]
		};

		TaskCompletionSource<IReadOnlyList<Game>> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		FakeAchievementService achievements = new(() => gate.Task)
		{
			SingleGame = _ => [refreshed]
		};
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		Task loading = main.HomeViewModel.ReloadGamesAsync();
		await WaitUntilAsync(() => achievements.ScanCalls == 1);

		// The full load will read the latest files anyway, so the change is deferred
		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");
		Assert.Equal(0, achievements.SingleGameCalls);

		gate.SetResult([initial]);
		await loading;

		Assert.Equal(1, achievements.SingleGameCalls);
		Assert.Equal(3, main.HomeViewModel.Games.Single().TotalCount);
		Assert.Equal(2, main.HomeViewModel.Games.Single().UnlockedCount);
	}

	[Fact]
	public async Task RefreshGameAsync_KeepsDuplicatesMergedAcrossSources()
	{
		AppSettings settings = new()
		{
			Achievements = new AchievementsSettings { MergeDuplicate = true, HideZeroPercent = false }
		};
		FakeSettingsService settingsService = new() { Settings = settings };
		AchievementService merger = new([], [], settingsService);

		Game emulator = new()
		{
			AppId = "480",
			Name = "Emulator",
			Source = AchievementSource.SteamEmulator,
			Achievements = [Unlocked("A"), Locked("B"), Locked("C")]
		};
		Game official = new()
		{
			AppId = "480",
			Name = "Official",
			Source = AchievementSource.SteamOfficial,
			Achievements = [Unlocked("B")]
		};
		Game emulatorUpdated = new()
		{
			AppId = "480",
			Name = "Emulator",
			Source = AchievementSource.SteamEmulator,
			Achievements = [Unlocked("A"), Locked("B"), Unlocked("C")]
		};

		FakeAchievementService achievements = new(
			() => Task.FromResult<IReadOnlyList<Game>>([emulator, official]))
		{
			Merge = merger.MergeGames,
			SingleGame = _ => [emulatorUpdated]
		};
		MainViewModel main = new(achievements, settingsService, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		GameItemViewModel before = Assert.Single(main.HomeViewModel.Games);
		Assert.Equal(3, before.TotalCount);
		Assert.Equal(2, before.UnlockedCount);

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		GameItemViewModel after = Assert.Single(main.HomeViewModel.Games);
		Assert.Equal(3, after.TotalCount);
		Assert.Equal(3, after.UnlockedCount);
	}

	[Fact]
	public async Task RefreshGameAsync_WithHideZeroPercentOn_HidesAGameThatLostItsUnlocks()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A"), Locked("B")] };
		Game refreshed = new() { AppId = "480", Name = "Half-Life", Achievements = [Locked("A"), Locked("B")] };

		AppSettings settings = new()
		{
			Achievements = new AchievementsSettings { HideZeroPercent = true, MergeDuplicate = true }
		};
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => [refreshed]
		};
		MainViewModel main = new(achievements, new FakeSettingsService { Settings = settings }, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Single(main.HomeViewModel.Games);

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.Empty(main.HomeViewModel.Games);
		Assert.Equal(1, main.HomeViewModel.TotalGameCount);
	}

	[Fact]
	public async Task RefreshGameAsync_WhenTheOpenDetailPageLosesItsGame_GoesBackHome()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A")] };
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => []
		};
		FakeSettingsService settingsService = new();
		MainViewModel main = new(achievements, settingsService, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		main.NavigateToGameDetail(new GameDetailViewModel(initial, showHidden: false, settingsService));
		Assert.IsType<GameDetailViewModel>(main.CurrentView);
		Assert.True(main.CanGoBack);

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.IsType<HomeViewModel>(main.CurrentView);
		Assert.False(main.CanGoBack);
	}

	[Fact]
	public async Task RefreshGameAsync_WhenTheOpenDetailPageBecomesEmpty_GoesBackHome()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A"), Locked("B")] };
		Game refreshed = new() { AppId = "480", Name = "Half-Life", Achievements = [Locked("A"), Locked("B")] };

		AppSettings settings = new()
		{
			Achievements = new AchievementsSettings { HideZeroPercent = true, MergeDuplicate = true }
		};
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => [refreshed]
		};
		FakeSettingsService settingsService = new() { Settings = settings };
		MainViewModel main = new(achievements, settingsService, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		main.NavigateToGameDetail(new GameDetailViewModel(initial, false, settingsService));

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.IsType<HomeViewModel>(main.CurrentView);
		Assert.False(main.CanGoBack);
	}

	[Fact]
	public async Task RefreshGameAsync_UpdatesADetailPageThatIsSuspended()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A"), Locked("B")] };
		Game refreshed = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements = [Unlocked("A"), Unlocked("B")]
		};

		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => [refreshed]
		};
		FakeSettingsService settingsService = new();
		MainViewModel main = new(achievements, settingsService, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		GameDetailViewModel detail = new(initial, false, settingsService);
		main.NavigateToGameDetail(detail);
		main.SuspendView();

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		Assert.Equal(2, detail.UnlockedCount);
	}

	[Fact]
	public async Task RefreshGameAsync_WhenTheOpenDetailPageGetsNewData_RefreshesIt()
	{
		Game initial = new() { AppId = "480", Name = "Half-Life", Achievements = [Unlocked("A"), Locked("B")] };
		Game refreshed = new()
		{
			AppId = "480",
			Name = "Half-Life",
			Achievements = [Unlocked("A"), Unlocked("B"), Locked("C")]
		};

		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([initial]))
		{
			SingleGame = _ => [refreshed]
		};
		FakeSettingsService settingsService = new();
		MainViewModel main = new(achievements, settingsService, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		main.NavigateToGameDetail(new GameDetailViewModel(initial, showHidden: false, settingsService));

		await main.HomeViewModel.RefreshGameAsync(AchievementSource.SteamEmulator, "480");

		GameDetailViewModel detail = Assert.IsType<GameDetailViewModel>(main.CurrentView);
		Assert.Equal(3, detail.TotalCount);
		Assert.Equal(2, detail.UnlockedCount);
	}

	[Fact]
	public async Task SearchText_FiltersTheListAndClearingItRestoresIt()
	{
		Game alpha = new() { AppId = "1", Name = "Alpha", Achievements = [Unlocked("A")] };
		Game beta = new() { AppId = "2", Name = "Beta", Achievements = [Unlocked("A")] };
		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([alpha, beta]));
		MainViewModel main = new(achievements, new FakeSettingsService(), new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Equal(2, main.HomeViewModel.Games.Count);

		main.HomeViewModel.SearchText = "alp";
		Assert.Single(main.HomeViewModel.Games);
		Assert.Equal("Alpha", main.HomeViewModel.Games[0].Name);

		main.HomeViewModel.SearchText = string.Empty;
		Assert.Equal(2, main.HomeViewModel.Games.Count);
	}

	[Fact]
	public async Task SortByCompletion_ReordersAndPersistsTheSetting()
	{
		AppSettings settings = new()
		{
			HomeSort = new SortSettings { Mode = "Alphabetical", Ascending = true }
		};
		Game complete = new() { AppId = "1", Name = "Zeta", Achievements = [Unlocked("A"), Unlocked("B")] };
		Game partial = new() { AppId = "2", Name = "Alpha", Achievements = [Unlocked("C"), Locked("D")] };

		FakeAchievementService achievements = new(() => Task.FromResult<IReadOnlyList<Game>>([partial, complete]));
		MainViewModel main = new(achievements, new FakeSettingsService { Settings = settings }, new FakeSteamApiService());

		await main.HomeViewModel.ReloadGamesAsync();
		Assert.Equal("Alpha", main.HomeViewModel.Games[0].Name);

		main.HomeViewModel.SortByCompletionCommand.Execute(null);

		Assert.Equal("Zeta", main.HomeViewModel.Games[0].Name);
		Assert.Equal("Completion", settings.HomeSort.Mode);
		Assert.True(settings.HomeSort.Ascending);

		main.HomeViewModel.SortByCompletionCommand.Execute(null);

		Assert.Equal("Alpha", main.HomeViewModel.Games[0].Name);
		Assert.False(settings.HomeSort.Ascending);
	}
}
