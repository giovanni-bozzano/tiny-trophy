using TinyTrophy.Services;
using TinyTrophy.Services.Enrichers;
using TinyTrophy.Services.Scanners;
using TinyTrophy.Services.Watchers;
using TinyTrophy.ViewModels;

namespace TinyTrophy;

/// <summary>
/// Composition root of the application: builds the object graph once and owns its lifetime.
/// </summary>
/// <remarks>
/// Wiring lives here rather than in <see cref="App"/> so that startup keeps to window and tray
/// concerns. Services are created but never started, leaving the caller in charge of when work begins.
/// </remarks>
public sealed class AppServices
	: IDisposable
{
	/// <summary>
	/// Application settings, already loaded from disk.
	/// </summary>
	public SettingsService Settings { get; }

	public SteamApiService SteamApi { get; }

	public IAchievementService Achievements { get; }

	public MainViewModel MainViewModel { get; }

	/// <summary>
	/// Watches for achievement changes. Call <see cref="IGameWatcherService.Start"/> once its events are wired.
	/// </summary>
	public IGameWatcherService GameWatcher { get; }

	/// <param name="checkForUpdateAsync">Runs the update check requested from the UI.</param>
	public AppServices(Func<Task> checkForUpdateAsync)
	{
		// Settings load synchronously, since every other service reads them while being built
		Settings = new SettingsService();
		Settings.LoadAsync().GetAwaiter().GetResult();

		SteamApi = new SteamApiService(Settings);

		List<IAchievementScanner> scanners =
		[
			new SteamEmulatorScanner(Settings),
			new ShadPs4Scanner(Settings),
			new SteamOfficialScanner(Settings, SteamApi)
		];

		List<IGameEnricher> enrichers =
		[
			new SteamGameEnricher(SteamApi)
		];

		Achievements = new AchievementService(scanners, enrichers, Settings);
		MainViewModel = new MainViewModel(Achievements, Settings, SteamApi, checkForUpdateAsync);

		List<IGameWatcher> watchers =
		[
			new SteamEmulatorWatcher(Settings, SteamApi),
			new ShadPs4Watcher()
		];
		GameWatcher = new GameWatcherService(watchers);
		MainViewModel.SetGameWatcher(GameWatcher);
	}

	public void Dispose()
	{
		GameWatcher.Dispose();
		SteamApi.Dispose();
	}
}
