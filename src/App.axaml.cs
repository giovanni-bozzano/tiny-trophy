using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Win32;
using System.Diagnostics;
using TinyTrophy.Models;
using TinyTrophy.Services;
using TinyTrophy.Services.Enrichers;
using TinyTrophy.Services.Scanners;
using TinyTrophy.Services.Watchers;
using TinyTrophy.ViewModels;
using TinyTrophy.Views;

namespace TinyTrophy;

public partial class App
	: Application
{
	private IGameWatcherService? _gameWatcher;
	private TrayIcon? _trayIcon;
	private MainWindow? _mainWindow;
	private SettingsService? _settingsService;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);

		// Images are cached to disk only, allowing the GC to reclaim off-screen bitmaps
		ImageLoader.AsyncImageLoader = new DiskOnlyImageLoader();

		// Delete leftover files from a previous update
		UpdateService.CleanupPreviousUpdate();

		// Silently delete the updated flag written by the previous version before restarting
		UpdateService.ConsumeUpdatedFlag();
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// Keep the app running when all windows close (lives in the system tray)
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

			// Load settings synchronously to ensure they're ready before anything else runs
			SettingsService settingsService = new();
			settingsService.LoadAsync().GetAwaiter().GetResult();
			_settingsService = settingsService;

			SteamApiService steamApi = new(settingsService);

			List<IAchievementScanner> scanners =
			[
				new SteamEmulatorScanner(settingsService),
				new ShadPs4Scanner(settingsService),
				new SteamOfficialScanner(settingsService, steamApi)
			];

			List<IGameEnricher> enrichers =
			[
				new SteamGameEnricher(steamApi)
			];

			AchievementService achievementService = new(scanners, enrichers, settingsService);

			MainViewModel mainViewModel = new(achievementService, settingsService, steamApi, async () => await CheckForUpdateAsync(_mainWindow!));
			_mainWindow = new MainWindow(mainViewModel);

			// Set up system tray icon
			SetupTrayIcon();

			desktop.ShutdownRequested += (_, _) =>
			{
				_gameWatcher?.Dispose();
				_trayIcon?.Dispose();
			};

			// Show setup if no API key has been configured yet
			if (string.IsNullOrWhiteSpace(settingsService.Settings.SteamApiKey))
			{
				mainViewModel.ShowSetup(async (key, steamId) =>
				{
					bool changed = false;
					if (key is not null)
					{
						settingsService.Settings.SteamApiKey = key;
						changed = true;
					}
					if (steamId is not null)
					{
						settingsService.Settings.SteamId = steamId;
						changed = true;
					}
					if (changed)
					{
						await settingsService.SaveAsync();
						steamApi.ClearCache();
					}

					StartServices(settingsService, steamApi, mainViewModel);
				});
			}
			else
			{
				StartServices(settingsService, steamApi, mainViewModel);
			}

			// Show window unless "Start minimized" is enabled
			if (!settingsService.Settings.StartMinimized)
				_mainWindow.Show();

			// Check for updates in the background
			if (settingsService.Settings.CheckForUpdates)
				_ = CheckForUpdateAsync(_mainWindow);
		}

		base.OnFrameworkInitializationCompleted();
	}

	private void StartServices(
		SettingsService settingsService,
		SteamApiService steamApi,
		MainViewModel mainViewModel)
	{
		SteamEmulatorWatcher steamEmulatorWatcher = new(settingsService, steamApi);
		ShadPs4Watcher shadPs4Watcher = new();

		List<IGameWatcher> watchers = [steamEmulatorWatcher, shadPs4Watcher];

		_gameWatcher = new GameWatcherService(watchers);
		mainViewModel.SetGameWatcher(_gameWatcher);

		_gameWatcher.AchievementsChanged += (_, _) =>
		{
			Dispatcher.UIThread.Post(() =>
			{
				_ = mainViewModel.HomeViewModel.LoadGamesCommand.ExecuteAsync(null);
			});
		};

		_gameWatcher.AchievementUnlocked += (_, e) =>
		{
			Debug.WriteLine($"[Achievement Unlocked] AppID={e.AppId} | {e.Achievement.Id} ({e.Achievement.Name})");

			if (settingsService.Settings.Notifications.Enabled)
			{
				bool playSound = settingsService.Settings.Notifications.PlaySound;
				Dispatcher.UIThread.Post(() =>
				{
					AchievementNotificationWindow.ShowNotification(e.Achievement, playSound);
				});
			}
		};

		steamEmulatorWatcher.ApiKeyValidated += (_, result) =>
		{
			Dispatcher.UIThread.Post(() =>
			{
				mainViewModel.ApiKeyWarning = ApiKeyWarningMessages.FromResult(result);
			});
		};

		_gameWatcher.Start();

		_ = mainViewModel.HomeViewModel.LoadGamesCommand.ExecuteAsync(null);
	}

	private void SetupTrayIcon()
	{
		NativeMenuItem showItem = new("Open TinyTrophy");
		showItem.Click += (_, _) => ShowMainWindow();

		NativeMenuItem startupItem = new();
		UpdateStartupMenuText(startupItem);
		startupItem.Click += (_, _) =>
		{
			SetStartupEnabled(!IsStartupEnabled());
			UpdateStartupMenuText(startupItem);
		};

		NativeMenuItem minimizedItem = new();
		UpdateMinimizedMenuText(minimizedItem);
		minimizedItem.Click += (_, _) =>
		{
			if (_settingsService is not null)
			{
				_settingsService.Settings.StartMinimized = !_settingsService.Settings.StartMinimized;
				_ = _settingsService.SaveAsync();
				UpdateMinimizedMenuText(minimizedItem);
			}
		};

		NativeMenuItem exitItem = new("Exit");
		exitItem.Click += (_, _) => ExitApp();

		NativeMenu menu =
		[
			showItem,
			new NativeMenuItemSeparator(),
			startupItem,
			minimizedItem,
			new NativeMenuItemSeparator(),
			exitItem
		];

		_trayIcon = new TrayIcon
		{
			Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://TinyTrophy/Assets/Icon.ico"))),
			ToolTipText = "TinyTrophy",
			Menu = menu,
			IsVisible = true
		};

		_trayIcon.Clicked += (_, _) => ShowMainWindow();
	}

	private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
	private const string StartupValueName = "TinyTrophy";

	private static void UpdateStartupMenuText(NativeMenuItem item)
	{
		item.Header = IsStartupEnabled() ? "✓ Run on startup" : "Run on startup";
	}

	private void UpdateMinimizedMenuText(NativeMenuItem item)
	{
		bool enabled = _settingsService?.Settings.StartMinimized ?? false;
		item.Header = enabled ? "✓ Start minimized" : "Start minimized";
	}

	internal static bool IsStartupEnabled()
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
		return key?.GetValue(StartupValueName) is not null;
	}

	internal static void SetStartupEnabled(bool enabled)
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
		if (key is null)
			return;

		if (enabled)
		{
			string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
			if (exePath is not null)
				key.SetValue(StartupValueName, $"\"{exePath}\"");
		}
		else
		{
			key.DeleteValue(StartupValueName, throwOnMissingValue: false);
		}
	}

	private void ShowMainWindow()
	{
		if (_mainWindow is null)
			return;

		Dispatcher.UIThread.Post(() =>
		{
			_mainWindow.Show();
			if (_mainWindow.WindowState == WindowState.Minimized)
				_mainWindow.WindowState = WindowState.Normal;
			_mainWindow.Activate();
		});
	}

	private void ExitApp()
	{
		Dispatcher.UIThread.Post(() =>
		{
			_mainWindow?.AllowClose = true;

			_gameWatcher?.Dispose();
			_trayIcon?.Dispose();

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
				desktop.Shutdown();
		});
	}

	private static async Task CheckForUpdateAsync(Window owner)
	{
		GitHubRelease? release = await UpdateService.CheckForUpdateAsync();
		if (release is null)
			return;

		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			if (!owner.IsVisible)
			{
				owner.Show();
				owner.Activate();
			}

			UpdateDialog dialog = new(release);
			dialog.ShowDialog(owner);
		});
	}
}
