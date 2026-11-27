using System.Text.Json;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure;

public interface ISettingsService
{
	AppSettings Settings { get; }
	Task LoadAsync();
	Task SaveAsync();
}

public sealed class SettingsService
	: ISettingsService
{
	private static readonly string DefaultSettingsFile = Path.Combine(AppPaths.DataDir, "settings.json");

	private readonly string _settingsFile;
	private readonly string _settingsTempFile;

	/// <summary>
	/// Serializes saves, so two overlapping ones cannot write over each other.
	/// </summary>
	private readonly SemaphoreSlim _saveGate = new(1, 1);

	/// <param name="settingsFile">
	/// Overrides the settings file location, which tests use to stay out of the real profile directory.
	/// The app always uses the default path.
	/// </param>
	public SettingsService(string? settingsFile = null)
	{
		_settingsFile = settingsFile ?? DefaultSettingsFile;
		_settingsTempFile = _settingsFile + ".tmp";
	}

	public AppSettings Settings { get; private set; } = new();

	public async Task LoadAsync()
	{
		if (!File.Exists(_settingsFile))
		{
			Settings = new AppSettings
			{
				WatchedDirectories = SteamEmulatorScanner.GetDefaultWatchedDirectories(),
				ProtonPrefixDirectories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories()
			};
			return;
		}

		const int maxAttempts = 3;
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				string json = await File.ReadAllTextAsync(_settingsFile).ConfigureAwait(false);
				Settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings) ?? new AppSettings();
				break;
			}
			catch (JsonException)
			{
				// Genuinely invalid JSON: preserve it so it can be recovered
				BackUpUnreadableSettings();
				Settings = new AppSettings();
				break;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (attempt < maxAttempts)
				{
					await Task.Delay(50).ConfigureAwait(false);
					continue;
				}

				// A transient lock (antivirus, backup, sync client) is not corruption: leave the file in
				// place and use defaults for this run instead of renaming a valid configuration.
				Settings = new AppSettings();
				break;
			}
		}

		MergeDefaultDirectories();
	}

	/// <summary>
	/// Combines the hard-coded default directories with the user's customizations, restoring the
	/// disabled state of defaults they switched off.
	/// </summary>
	private void MergeDefaultDirectories()
	{
		List<DirectoryConfig> defaultWatchedDirectories = SteamEmulatorScanner.GetDefaultWatchedDirectories();
		List<DirectoryConfig> savedWatchedDirectories = Settings.WatchedDirectories;

		HashSet<string> disabledWatchedDirectories = [.. savedWatchedDirectories
			.Where(d => d.IsDefault && !d.Enabled)
			.Select(d => d.Path)];

		foreach (DirectoryConfig watchedDirectory in defaultWatchedDirectories)
		{
			if (disabledWatchedDirectories.Contains(watchedDirectory.Path))
				watchedDirectory.Enabled = false;
		}

		List<DirectoryConfig> customFolders = [.. savedWatchedDirectories.Where(d => !d.IsDefault)];
		Settings.WatchedDirectories = [.. defaultWatchedDirectories, .. customFolders];

		List<DirectoryConfig> defaultProtonPrefixDirectories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories();
		List<DirectoryConfig> savedProtonPrefixDirectories = Settings.ProtonPrefixDirectories;

		HashSet<string> disabledProtonPrefixDirectories = [.. savedProtonPrefixDirectories
			.Where(d => d.IsDefault && !d.Enabled)
			.Select(d => d.Path)];

		foreach (DirectoryConfig protonPrefixDirectory in defaultProtonPrefixDirectories)
		{
			if (disabledProtonPrefixDirectories.Contains(protonPrefixDirectory.Path))
				protonPrefixDirectory.Enabled = false;
		}

		List<DirectoryConfig> customProtonPrefixDirectories = [.. savedProtonPrefixDirectories.Where(d => !d.IsDefault)];
		Settings.ProtonPrefixDirectories = [.. defaultProtonPrefixDirectories, .. customProtonPrefixDirectories];
	}

	public async Task SaveAsync()
	{
		await _saveGate.WaitAsync();
		try
		{
			Directory.CreateDirectory(AppPaths.DataDir);

			// Serialized from a copy, so the live settings are never mutated into their persisted form
			// while watchers and scanners are reading them on other threads
			string json = JsonSerializer.Serialize(BuildPersistedSettings(Settings), AppJsonContext.Default.AppSettings);

			// Written aside and moved into place: a crash halfway through cannot truncate the live file
			await File.WriteAllTextAsync(_settingsTempFile, json);
			File.Move(_settingsTempFile, _settingsFile, overwrite: true);
		}
		finally
		{
			_saveGate.Release();
		}

		SteamEmulatorScanner.ClearResolvedDirectoriesCache();
	}

	/// <summary>
	/// Builds the settings that are persisted. Only custom folders (stored with portable paths) and
	/// explicitly disabled defaults are saved, because the defaults themselves are re-added on load.
	/// </summary>
	private static AppSettings BuildPersistedSettings(AppSettings settings)
	{
		HashSet<string> currentDefaultWatchedDirectories = [.. SteamEmulatorScanner.GetDefaultWatchedDirectories().Select(d => d.Path)];
		HashSet<string> currentDefaultProtonPrefixDirectories = [.. SteamEmulatorScanner.GetDefaultProtonPrefixDirectories().Select(d => d.Path)];

		return new AppSettings
		{
			Achievements = new AchievementsSettings
			{
				ShowHidden = settings.Achievements.ShowHidden,
				MergeDuplicate = settings.Achievements.MergeDuplicate,
				HideZeroPercent = settings.Achievements.HideZeroPercent
			},
			Notifications = new NotificationSettings
			{
				Enabled = settings.Notifications.Enabled,
				PlaySound = settings.Notifications.PlaySound
			},
			HomeSort = new SortSettings
			{
				Mode = settings.HomeSort.Mode,
				Ascending = settings.HomeSort.Ascending
			},
			GameDetailSort = new SortSettings
			{
				Mode = settings.GameDetailSort.Mode,
				Ascending = settings.GameDetailSort.Ascending
			},
			WatchedDirectories = [.. settings.WatchedDirectories
				.Where(d => !d.IsDefault || (!d.Enabled && currentDefaultWatchedDirectories.Contains(d.Path)))
				.Select(ToPersistedDirectory)],
			ProtonPrefixDirectories = [.. settings.ProtonPrefixDirectories
				.Where(d => !d.IsDefault || (!d.Enabled && currentDefaultProtonPrefixDirectories.Contains(d.Path)))
				.Select(ToPersistedDirectory)],
			SteamApiKey = settings.SteamApiKey,
			SteamId = settings.SteamId,
			Language = settings.Language,
			SteamOfficialEnabled = settings.SteamOfficialEnabled,
			ShadPs4Enabled = settings.ShadPs4Enabled,
			StartMinimized = settings.StartMinimized,
			CloseToTray = settings.CloseToTray,
			CheckForUpdates = settings.CheckForUpdates
		};
	}

	private static DirectoryConfig ToPersistedDirectory(DirectoryConfig directory) => new()
	{
		Path = directory.IsDefault ? directory.Path : SteamEmulatorScanner.CollapsePath(directory.Path),
		Label = directory.Label,
		Enabled = directory.Enabled,
		IsDefault = directory.IsDefault
	};

	/// <summary>
	/// Moves an unreadable settings file aside, so it can be recovered and the next save cannot destroy it.
	/// </summary>
	private void BackUpUnreadableSettings()
	{
		try
		{
			string backupPath = _settingsFile + ".corrupt";
			if (File.Exists(backupPath))
				backupPath = _settingsFile + $".corrupt-{Guid.NewGuid():N}";

			File.Move(_settingsFile, backupPath);
		}
		catch { }
	}
}
