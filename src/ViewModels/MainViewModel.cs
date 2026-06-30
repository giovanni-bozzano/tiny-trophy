using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyTrophy.Services;

namespace TinyTrophy.ViewModels;

public sealed partial class MainViewModel
	: ObservableObject
{
	private readonly ISteamApiService _steamApi;
	private IGameWatcherService? _gameWatcher;

	[ObservableProperty]
	public partial object? CurrentView { get; set; }

	[ObservableProperty]
	public partial bool CanGoBack { get; set; }

	[ObservableProperty]
	public partial bool ShowNavBar { get; set; } = true;

	[ObservableProperty]
	public partial string ApiKeyWarning { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; private set; }

	public HomeViewModel HomeViewModel { get; }
	public SettingsViewModel SettingsViewModel { get; }

	public MainViewModel(
		IAchievementService achievementService,
		ISettingsService settingsService,
		ISteamApiService steamApi,
		Func<Task>? checkForUpdate = null)
	{
		_steamApi = steamApi;

		HomeViewModel = new HomeViewModel(achievementService, this, settingsService, steamApi);
		SettingsViewModel = new SettingsViewModel(settingsService, this, checkForUpdate);

		HomeViewModel.LoadGamesCommand.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(HomeViewModel.LoadGamesCommand.IsRunning))
				IsBusy = HomeViewModel.LoadGamesCommand.IsRunning;
		};

		CurrentView = HomeViewModel;
	}

	public void SetGameWatcher(IGameWatcherService watcher) => _gameWatcher = watcher;

	/// <summary>
	/// Restarts file watchers and reloads the game list. Called after watched folder settings change.
	/// </summary>
	public async Task ReloadWatchedFoldersAsync()
	{
		_gameWatcher?.Restart();
		CurrentView = HomeViewModel;
		CanGoBack = false;
		await HomeViewModel.LoadGamesCommand.ExecuteAsync(null);
	}

	public void ShowSetup(Action<string?, string?> onComplete)
	{
		ShowNavBar = false;
		CurrentView = new InitialSetupViewModel((key, steamId) =>
		{
			onComplete(key, steamId);
			ShowNavBar = true;
			CurrentView = HomeViewModel;
		});
	}

	[RelayCommand]
	private async Task RefreshMetadataAsync()
	{
		_steamApi.ClearCache();
		CurrentView = HomeViewModel;
		CanGoBack = false;
		await HomeViewModel.LoadGamesCommand.ExecuteAsync(null);
	}

	[RelayCommand]
	private void GoBack()
	{
		CurrentView = HomeViewModel;
		CanGoBack = false;
	}

	[RelayCommand]
	private void OpenSettings()
	{
		SettingsViewModel.LoadFromSettings();
		CurrentView = SettingsViewModel;
		CanGoBack = true;
	}

	public void NavigateToGameDetail(GameDetailViewModel vm)
	{
		CurrentView = vm;
		CanGoBack = true;
	}
}
