using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Tests;

public sealed class SettingsViewModelTests
{
	private sealed record Harness(
		SettingsViewModel ViewModel,
		FakeSettingsService SettingsService,
		MainViewModel Main,
		FakeSteamApiService SteamApi);

	private static Harness Create(
		AppSettings? settings = null,
		Func<Task>? checkForUpdate = null)
	{
		FakeSettingsService settingsService = new() { Settings = settings ?? new AppSettings() };
		FakeSteamApiService steamApi = new();
		MainViewModel main = new(new FakeAchievementService(), settingsService, steamApi);
		SettingsViewModel viewModel = new(settingsService, main, checkForUpdate);
		return new Harness(viewModel, settingsService, main, steamApi);
	}

	[Fact]
	public void Constructor_CopiesEverySettingIntoTheForm()
	{
		AppSettings settings = new()
		{
			SteamApiKey = "KEY",
			SteamId = "765",
			Language = "german",
			Achievements = new AchievementsSettings
			{
				ShowHidden = true,
				MergeDuplicate = false,
				HideZeroPercent = false
			},
			Notifications = new NotificationSettings { Enabled = false, PlaySound = false },
			CheckForUpdates = false,
			CloseToTray = false,
			SteamOfficialEnabled = false,
			ShadPs4Enabled = false,
			WatchedDirectories = [new DirectoryConfig { Path = "a", Label = "A" }],
			ProtonPrefixDirectories = [new DirectoryConfig { Path = "b", Label = "B" }]
		};

		SettingsViewModel viewModel = Create(settings).ViewModel;

		Assert.Equal("KEY", viewModel.SteamApiKey);
		Assert.Equal("765", viewModel.SteamId);
		Assert.Equal("german", viewModel.Language);
		Assert.True(viewModel.ShowHidden);
		Assert.False(viewModel.MergeDuplicates);
		Assert.False(viewModel.HideZeroPercent);
		Assert.False(viewModel.NotificationsEnabled);
		Assert.False(viewModel.NotificationSound);
		Assert.False(viewModel.CheckForUpdates);
		Assert.False(viewModel.CloseToTray);
		Assert.False(viewModel.SteamOfficialEnabled);
		Assert.False(viewModel.ShadPs4Enabled);
		Assert.Equal("a", Assert.Single(viewModel.WatchedDirectories).Path);
		Assert.Equal("b", Assert.Single(viewModel.ProtonPrefixDirectories).Path);
	}

	[Fact]
	public void AddWatchedDirectory_UsesTheFolderNameWhenNoLabelIsGiven()
	{
		Harness harness = Create();
		string path = Path.Combine("saves", "Goldberg");

		harness.ViewModel.NewWatchedDirectoryPath = $"  {path}  ";
		harness.ViewModel.NewWatchedDirectoryLabel = "   ";
		harness.ViewModel.AddWatchedDirectoryCommand.Execute(null);

		DirectoryItemViewModel item = Assert.Single(harness.ViewModel.WatchedDirectories);
		Assert.Equal(path, item.Path);
		Assert.Equal("Goldberg", item.Label);
		Assert.True(item.Enabled);
		Assert.True(item.IsRemovable);
		Assert.Equal(string.Empty, harness.ViewModel.NewWatchedDirectoryPath);
		Assert.Equal(string.Empty, harness.ViewModel.NewWatchedDirectoryLabel);
	}

	[Fact]
	public void AddWatchedDirectory_WithACustomLabel_KeepsItTrimmed()
	{
		Harness harness = Create();
		harness.ViewModel.NewWatchedDirectoryPath = Path.Combine("saves", "Goldberg");
		harness.ViewModel.NewWatchedDirectoryLabel = "  My saves  ";

		harness.ViewModel.AddWatchedDirectoryCommand.Execute(null);

		Assert.Equal("My saves", Assert.Single(harness.ViewModel.WatchedDirectories).Label);
	}

	[Fact]
	public void AddWatchedDirectory_WithAnEmptyPath_DoesNothing()
	{
		Harness harness = Create();

		harness.ViewModel.NewWatchedDirectoryPath = "   ";
		harness.ViewModel.AddWatchedDirectoryCommand.Execute(null);

		Assert.Empty(harness.ViewModel.WatchedDirectories);
	}

	[Fact]
	public void RemoveWatchedDirectory_KeepsDefaults()
	{
		Harness harness = Create();
		DirectoryItemViewModel defaultItem = new(new DirectoryConfig
		{
			Path = "default",
			Label = "Default",
			IsDefault = true
		});
		harness.ViewModel.WatchedDirectories.Add(defaultItem);

		harness.ViewModel.RemoveWatchedDirectoryCommand.Execute(defaultItem);

		Assert.Single(harness.ViewModel.WatchedDirectories);
	}

	[Fact]
	public void RemoveWatchedDirectory_RemovesCustomEntries()
	{
		Harness harness = Create();
		harness.ViewModel.NewWatchedDirectoryPath = Path.Combine("saves", "Custom");
		harness.ViewModel.AddWatchedDirectoryCommand.Execute(null);
		DirectoryItemViewModel item = Assert.Single(harness.ViewModel.WatchedDirectories);

		harness.ViewModel.RemoveWatchedDirectoryCommand.Execute(item);

		Assert.Empty(harness.ViewModel.WatchedDirectories);
	}

	[Fact]
	public void AddProtonPrefixDirectory_DerivesTheLabelAndClearsTheInputs()
	{
		Harness harness = Create();
		string path = Path.Combine("prefixes", "Heroic");

		harness.ViewModel.NewProtonPrefixDirectoryPath = path;
		harness.ViewModel.AddProtonPrefixDirectoryCommand.Execute(null);

		DirectoryItemViewModel item = Assert.Single(harness.ViewModel.ProtonPrefixDirectories);
		Assert.Equal(path, item.Path);
		Assert.Equal("Heroic", item.Label);
		Assert.Equal(string.Empty, harness.ViewModel.NewProtonPrefixDirectoryPath);
	}

	[Fact]
	public void ResetDefaults_RestoresTheDefaultForm()
	{
		AppSettings settings = new()
		{
			SteamApiKey = "KEY",
			SteamId = "765",
			Achievements = new AchievementsSettings { MergeDuplicate = false, HideZeroPercent = false }
		};
		Harness harness = Create(settings);
		harness.ViewModel.WatchedDirectories.Add(new DirectoryItemViewModel(new DirectoryConfig { Path = "custom" }));

		harness.ViewModel.ResetDefaultsCommand.Execute(null);

		Assert.Equal(string.Empty, harness.ViewModel.SteamApiKey);
		Assert.Equal(string.Empty, harness.ViewModel.SteamId);
		Assert.True(harness.ViewModel.MergeDuplicates);
		Assert.True(harness.ViewModel.HideZeroPercent);
		Assert.Equal("Settings reset to defaults.", harness.ViewModel.StatusMessage);
		Assert.NotEmpty(harness.ViewModel.WatchedDirectories);
		Assert.NotEmpty(harness.ViewModel.ProtonPrefixDirectories);
		Assert.All(harness.ViewModel.WatchedDirectories, directory => Assert.True(directory.IsDefault));
		Assert.All(harness.ViewModel.ProtonPrefixDirectories, directory => Assert.True(directory.IsDefault));
		Assert.Equal(
			SteamEmulatorScanner.GetDefaultWatchedDirectories().Count,
			harness.ViewModel.WatchedDirectories.Count);
		Assert.Equal(
			SteamEmulatorScanner.GetDefaultProtonPrefixDirectories().Count,
			harness.ViewModel.ProtonPrefixDirectories.Count);

		// Resetting the form must not write to the persisted settings until Save is pressed
		Assert.Equal("KEY", harness.SettingsService.Settings.SteamApiKey);
	}

	[Fact]
	public async Task SaveSettingsAsync_PersistsTheFormAndAppliesIt()
	{
		Harness harness = Create();
		harness.ViewModel.SteamApiKey = "KEY";
		harness.ViewModel.SteamId = "765";
		harness.ViewModel.MergeDuplicates = false;
		harness.ViewModel.ShowHidden = true;
		harness.ViewModel.WatchedDirectories =
			[new DirectoryItemViewModel(new DirectoryConfig { Path = "p", Label = "P", Enabled = false })];

		await harness.ViewModel.SaveSettingsCommand.ExecuteAsync(null);

		Assert.Equal(1, harness.SettingsService.SaveCalls);
		Assert.Equal("KEY", harness.SettingsService.Settings.SteamApiKey);
		Assert.Equal("765", harness.SettingsService.Settings.SteamId);
		Assert.False(harness.SettingsService.Settings.Achievements.MergeDuplicate);
		Assert.True(harness.SettingsService.Settings.Achievements.ShowHidden);

		DirectoryConfig directory = Assert.Single(harness.SettingsService.Settings.WatchedDirectories);
		Assert.Equal("p", directory.Path);
		Assert.False(directory.Enabled);
		Assert.False(directory.IsDefault);

		// Applying the settings closes the settings screen and reloads the list
		Assert.IsType<HomeViewModel>(harness.Main.CurrentView);
	}

	[Fact]
	public async Task SaveSettingsAsync_TrimsTheCredentials()
	{
		Harness harness = Create();
		harness.ViewModel.SteamApiKey = "  KEY  ";
		harness.ViewModel.SteamId = " 765 ";

		await harness.ViewModel.SaveSettingsCommand.ExecuteAsync(null);

		Assert.Equal("KEY", harness.SettingsService.Settings.SteamApiKey);
		Assert.Equal("765", harness.SettingsService.Settings.SteamId);
		Assert.Equal("KEY", harness.ViewModel.SteamApiKey);
		Assert.Equal("765", harness.ViewModel.SteamId);
	}

	[Fact]
	public async Task SaveSettingsAsync_WhenTheSaveFails_ShowsTheError()
	{
		Harness harness = Create();
		harness.SettingsService.SaveError = new IOException("disk full");
		harness.ViewModel.SteamApiKey = "KEY";

		await harness.ViewModel.SaveSettingsCommand.ExecuteAsync(null);

		Assert.Equal(1, harness.SettingsService.SaveCalls);
		Assert.Contains("disk full", harness.ViewModel.StatusMessage);
	}

	[Fact]
	public async Task SaveSettingsAsync_ClearsTheCacheOnlyWhenTheApiKeyChanged()
	{
		Harness harness = Create();

		harness.ViewModel.SteamApiKey = "KEY";
		await harness.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
		Assert.Equal(1, harness.SteamApi.ClearCacheCalls);

		await harness.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
		Assert.Equal(1, harness.SteamApi.ClearCacheCalls);

		harness.ViewModel.SteamApiKey = "OTHER";
		await harness.ViewModel.SaveSettingsCommand.ExecuteAsync(null);
		Assert.Equal(2, harness.SteamApi.ClearCacheCalls);
	}

	[Fact]
	public async Task ClearCachesCommand_ForwardsToTheMainViewModel()
	{
		Harness harness = Create();

		await harness.ViewModel.ClearCachesCommand.ExecuteAsync(null);

		Assert.Equal(1, harness.SteamApi.ClearCacheCalls);
		Assert.IsType<HomeViewModel>(harness.Main.CurrentView);
	}

	[Fact]
	public async Task CheckForUpdateCommand_RunsTheInjectedCheck()
	{
		int calls = 0;
		Harness harness = Create(checkForUpdate: () =>
		{
			calls++;
			return Task.CompletedTask;
		});

		await harness.ViewModel.CheckForUpdateCommand.ExecuteAsync(null);

		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task CheckForUpdateCommand_WithoutACheck_DoesNothing()
	{
		Harness harness = Create();

		await harness.ViewModel.CheckForUpdateCommand.ExecuteAsync(null);
	}

	[Fact]
	public void DirectoryItemViewModel_ReportsRemovabilityAndRoundTripsToConfig()
	{
		DirectoryConfig config = new()
		{
			Path = "p",
			Label = "P",
			Enabled = false,
			IsDefault = true
		};
		DirectoryItemViewModel item = new(config);

		Assert.False(item.IsRemovable);

		DirectoryConfig roundTripped = item.ToConfig();

		Assert.Equal("p", roundTripped.Path);
		Assert.Equal("P", roundTripped.Label);
		Assert.False(roundTripped.Enabled);
		Assert.True(roundTripped.IsDefault);
	}
}
