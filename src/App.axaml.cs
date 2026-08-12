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
using TinyTrophy.ViewModels;
using TinyTrophy.Views;

namespace TinyTrophy;

public partial class App
	: Application
{
	private AppServices? _services;
	private TrayIcon? _trayIcon;
	private MainWindow? _mainWindow;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);

		// Images are resized once on download and cached to disk at the size they are drawn
		ImageLoader.AsyncImageLoader = ImageLoaders.Default;

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

			AppServices services = new(async () => await CheckForUpdateAsync(_mainWindow!));
			_services = services;

			SettingsService settingsService = services.Settings;
			MainViewModel mainViewModel = services.MainViewModel;
			_mainWindow = new MainWindow(mainViewModel);

			// Set up system tray icon
			SetupTrayIcon();

			desktop.ShutdownRequested += (_, _) =>
			{
				_services?.Dispose();
				_trayIcon?.Dispose();
				ImageLoaders.DisposeAll();
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
						services.SteamApi.ClearCache();
					}

					StartServices(services);
				});
			}
			else
			{
				StartServices(services);
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

	/// <summary>
	/// Connects the already built services to the UI and starts watching for achievements.
	/// </summary>
	private static void StartServices(AppServices services)
	{
		SettingsService settingsService = services.Settings;
		MainViewModel mainViewModel = services.MainViewModel;
		IGameWatcherService gameWatcher = services.GameWatcher;

		gameWatcher.AchievementsChanged += (_, _) =>
		{
			Dispatcher.UIThread.Post(() =>
			{
				_ = mainViewModel.HomeViewModel.LoadGamesCommand.ExecuteAsync(null);
			});
		};

		gameWatcher.AchievementUnlocked += (_, e) =>
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

		services.SteamApi.ApiKeyValidated += (_, result) =>
		{
			Dispatcher.UIThread.Post(() =>
			{
				mainViewModel.ApiKeyWarning = ApiKeyWarningMessages.FromResult(result);
			});
		};

		gameWatcher.Start();

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
		minimizedItem.Click += async (_, _) =>
		{
			if (_services is not null)
			{
				SettingsService settings = _services.Settings;
				settings.Settings.StartMinimized = !settings.Settings.StartMinimized;
				await settings.SaveAsync();
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
		bool enabled = _services?.Settings.Settings.StartMinimized ?? false;
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

			_services?.Dispose();
			_trayIcon?.Dispose();
			ImageLoaders.DisposeAll();

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
