using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Watchers;
using TinyTrophy.Models;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Tests;

public sealed class MainViewModelTests
{
	private sealed class FakeWatcherService
		: IGameWatcherService
	{
		public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
		public event EventHandler<GameChangedEventArgs>? AchievementsChanged;

		public int StartCalls;
		public int StopCalls;
		public int RestartCalls;
		public int DisposeCalls;
		public bool? LastReportChanges;

		public (int Added, int Removed) Changes { get; set; }

		public void Start() => StartCalls++;

		public void Stop() => StopCalls++;

		public (int Added, int Removed) Restart(bool reportChanges = true)
		{
			RestartCalls++;
			LastReportChanges = reportChanges;
			return Changes;
		}

		public void Dispose() => DisposeCalls++;

		/// <summary>
		/// Subscribing to the fakes only keeps the interface members reachable; raising them is a no-op.
		/// </summary>
		public void Raise()
		{
			AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs("480", new Achievement()));
			AchievementsChanged?.Invoke(this, new GameChangedEventArgs(AchievementSource.SteamEmulator, "480"));
		}
	}

	private static MainViewModel CreateViewModel(
		out FakeAchievementService achievements,
		out FakeSteamApiService steamApi)
	{
		achievements = new FakeAchievementService();
		steamApi = new FakeSteamApiService();
		return new MainViewModel(achievements, new FakeSettingsService(), steamApi);
	}

	[Fact]
	public async Task ApplySettingsAsync_NavigatesHomeReloadsAndClearsTheCacheWhenTheKeyChanged()
	{
		MainViewModel main = CreateViewModel(out FakeAchievementService achievements, out FakeSteamApiService steamApi);

		main.OpenSettingsCommand.Execute(null);
		Assert.IsType<SettingsViewModel>(main.CurrentView);
		Assert.True(main.CanGoBack);

		await main.ApplySettingsAsync(apiKeyChanged: true);

		Assert.IsType<HomeViewModel>(main.CurrentView);
		Assert.False(main.CanGoBack);
		Assert.Equal(1, achievements.ScanCalls);
		Assert.Equal(1, steamApi.ClearCacheCalls);
	}

	[Fact]
	public async Task ApplySettingsAsync_WithoutAKeyChange_KeepsTheCache()
	{
		MainViewModel main = CreateViewModel(out FakeAchievementService achievements, out FakeSteamApiService steamApi);

		await main.ApplySettingsAsync(apiKeyChanged: false);

		Assert.Equal(1, achievements.ScanCalls);
		Assert.Equal(0, steamApi.ClearCacheCalls);
	}

	[Fact]
	public async Task RescanPathsCommand_WithoutWatcherChanges_ReportsNoneAddedOrRemoved()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		using GameWatcherService watcher = new([]);
		main.SetGameWatcher(watcher);

		await main.RescanPathsCommand.ExecuteAsync(null);

		Assert.Equal("No games added or removed", main.RescanResult);
		Assert.True(main.IsRescanResultVisible);
	}

	[Fact]
	public async Task RescanPathsCommand_ReportsGamesThatWereAddedAndRemoved()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		FakeWatcherService watcher = new() { Changes = (2, 1) };
		main.SetGameWatcher(watcher);

		await main.RescanPathsCommand.ExecuteAsync(null);

		Assert.Equal(1, watcher.RestartCalls);
		Assert.Equal("2 games added, 1 removed", main.RescanResult);
	}

	[Fact]
	public async Task RescanPathsCommand_UsesTheSingularForOneGame()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		main.SetGameWatcher(new FakeWatcherService { Changes = (1, 0) });

		await main.RescanPathsCommand.ExecuteAsync(null);

		Assert.Equal("1 game added, 0 removed", main.RescanResult);
	}

	[Fact]
	public async Task RescanPathsCommand_WithoutAWatcher_DoesNothing()
	{
		MainViewModel main = CreateViewModel(out _, out _);

		await main.RescanPathsCommand.ExecuteAsync(null);

		Assert.Null(main.RescanResult);
	}

	[Fact]
	public async Task ApplySettingsAsync_RestartsTheWatcherWithoutReportingChanges()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		FakeWatcherService watcher = new();
		main.SetGameWatcher(watcher);

		await main.ApplySettingsAsync(apiKeyChanged: false);

		Assert.Equal(1, watcher.RestartCalls);
		Assert.False(watcher.LastReportChanges);
	}

	[Fact]
	public async Task ClearCachesCommand_ClearsTheCacheWithoutRestartingTheWatcher()
	{
		MainViewModel main = CreateViewModel(out FakeAchievementService achievements, out FakeSteamApiService steamApi);
		FakeWatcherService watcher = new();
		main.SetGameWatcher(watcher);

		await main.ClearCachesCommand.ExecuteAsync(null);

		Assert.Equal(1, steamApi.ClearCacheCalls);
		Assert.Equal(0, watcher.RestartCalls);
		Assert.Equal(1, achievements.ScanCalls);
		Assert.IsType<HomeViewModel>(main.CurrentView);
	}

	[Fact]
	public async Task RetrySteamCommand_ResetsTheCooldownAndReloadsWithoutClearingTheCache()
	{
		MainViewModel main = CreateViewModel(out FakeAchievementService achievements, out FakeSteamApiService steamApi);
		main.ApiKeyWarning = ApiKeyWarningMessages.Unreachable;

		await main.RetrySteamCommand.ExecuteAsync(null);

		Assert.Equal(1, steamApi.ResetOfflineCalls);
		Assert.Equal(1, achievements.ScanCalls);
		Assert.Equal(0, steamApi.ClearCacheCalls);
		Assert.Equal(string.Empty, main.ApiKeyWarning);
	}

	[Fact]
	public void CanRetrySteam_IsOfferedOnlyForConnectivityWarnings()
	{
		MainViewModel main = CreateViewModel(out _, out _);

		main.ApiKeyWarning = ApiKeyWarningMessages.Invalid;
		Assert.False(main.CanRetrySteam);

		main.ApiKeyWarning = "";
		Assert.False(main.CanRetrySteam);

		main.ApiKeyWarning = ApiKeyWarningMessages.Unreachable;
		Assert.True(main.CanRetrySteam);
	}

	[Fact]
	public void ShowSetup_HidesTheNavBarUntilItCompletes()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		string? receivedKey = null;
		string? receivedId = null;

		main.ShowSetup((key, id) =>
		{
			receivedKey = key;
			receivedId = id;
		});

		Assert.IsType<InitialSetupViewModel>(main.CurrentView);
		Assert.False(main.ShowNavBar);

		InitialSetupViewModel setup = Assert.IsType<InitialSetupViewModel>(main.CurrentView);
		setup.ApiKey = " KEY ";
		setup.SteamId = " 76561198000000000 ";
		setup.SaveCommand.Execute(null);

		Assert.Equal("KEY", receivedKey);
		Assert.Equal("76561198000000000", receivedId);
		Assert.True(main.ShowNavBar);
		Assert.IsType<HomeViewModel>(main.CurrentView);
	}

	[Fact]
	public void ShowSetup_WithAnEmptySteamId_PassesNull()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		string? receivedId = "unset";

		main.ShowSetup((_, id) => receivedId = id);

		InitialSetupViewModel setup = Assert.IsType<InitialSetupViewModel>(main.CurrentView);
		setup.SteamId = "   ";
		setup.SaveCommand.Execute(null);

		Assert.Null(receivedId);
	}

	[Fact]
	public void SuspendView_KeepsTheViewActiveWhileClearingIt()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		HomeViewModel home = main.HomeViewModel;

		main.SuspendView();

		Assert.Null(main.CurrentView);
		Assert.Same(home, main.ActiveView);

		main.ResumeView();

		Assert.Same(home, main.CurrentView);
		Assert.Same(home, main.ActiveView);
	}

	[Fact]
	public void SuspendAndResume_RestoresAnOpenDetailPage()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		GameDetailViewModel detail = new(
			new Game { AppId = "1", Name = "Game" },
			showHidden: false,
			new FakeSettingsService());

		main.NavigateToGameDetail(detail);
		main.SuspendView();

		Assert.Same(detail, main.ActiveView);

		main.ResumeView();

		Assert.Same(detail, main.CurrentView);
		Assert.True(main.CanGoBack);
	}

	[Fact]
	public void GoBack_WhileSuspended_ReplacesTheSuspendedDetailWithHome()
	{
		MainViewModel main = CreateViewModel(out _, out _);
		GameDetailViewModel detail = new(
			new Game { AppId = "1", Name = "Game" },
			false,
			new FakeSettingsService());

		main.NavigateToGameDetail(detail);
		main.SuspendView();
		main.GoBackCommand.Execute(null);
		main.ResumeView();

		Assert.IsType<HomeViewModel>(main.CurrentView);
		Assert.False(main.CanGoBack);
	}

	[Fact]
	public void GoBack_FromSettings_ReturnsHome()
	{
		MainViewModel main = CreateViewModel(out _, out _);

		main.OpenSettingsCommand.Execute(null);
		main.GoBackCommand.Execute(null);

		Assert.IsType<HomeViewModel>(main.CurrentView);
		Assert.False(main.CanGoBack);
	}
}
