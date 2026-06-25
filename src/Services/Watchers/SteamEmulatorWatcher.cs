using TinyTrophy.Models;
using TinyTrophy.Services.Scanners;

namespace TinyTrophy.Services.Watchers;

/// <summary>
/// Watches Steam emulator watched folders for achievement file changes.
/// Detects new unlocks, enriches them with Steam API metadata, and fires notification events.
/// </summary>
public sealed class SteamEmulatorWatcher(
	ISettingsService settings,
	ISteamApiService steamApi)
	: GameWatcherBase
{
	/// <summary>
	/// Fired after the API key is validated during achievement detection.
	/// </summary>
	public event EventHandler<ApiKeyValidationResult>? ApiKeyValidated;

	protected override void InitializeKnownState()
	{
		foreach (WatchedFolderConfig folder in settings.Settings.WatchedFolders)
		{
			if (!folder.Enabled || string.IsNullOrWhiteSpace(folder.Path))
				continue;

			string resolved = SteamEmulatorScanner.ExpandPath(folder.Path);
			if (!Directory.Exists(resolved))
				continue;

			try
			{
				foreach (string appDir in Directory.EnumerateDirectories(resolved))
				{
					string appId = Path.GetFileName(appDir);
					if (!AchievementFileParser.IsAppId(appId))
						continue;

					HashSet<string> unlocked = AchievementFileParser.ParseFromDirectory(appDir)
						.Where(a => a.IsUnlocked)
						.Select(a => a.Id)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);

					SetKnownUnlocks(appId, unlocked);
				}
			}
			catch { }
		}
	}

	protected override void SetupFileWatchers()
	{
		foreach (WatchedFolderConfig folder in settings.Settings.WatchedFolders)
		{
			if (!folder.Enabled || string.IsNullOrWhiteSpace(folder.Path))
				continue;

			string resolved = SteamEmulatorScanner.ExpandPath(folder.Path);
			if (!Directory.Exists(resolved))
				continue;

			try
			{
				FileSystemWatcher watcher = new(resolved)
				{
					IncludeSubdirectories = true,
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
					EnableRaisingEvents = true
				};

				watcher.Changed += OnFileChanged;
				watcher.Created += OnFileChanged;
				watcher.Deleted += OnFileChanged;
				AddWatcher(watcher);
			}
			catch { }
		}
	}

	private void OnFileChanged(
		object sender,
		FileSystemEventArgs e)
	{
		if (!AchievementFileParser.IsAchievementFile(Path.GetFileName(e.FullPath)))
			return;

		string? appDir = FindAppIdDirectory(e.FullPath);
		if (appDir is null)
			return;

		string appId = Path.GetFileName(appDir);
		ScheduleDetection(appId);
	}

	protected override async Task DetectNewAchievementsAsync(string key)
	{
		try
		{
			// Find the actual directory for this appId across all watched folders
			string? appDir = FindAppDirectory(key);
			if (appDir is null)
				return;

			List<Achievement> currentUnlocked = [.. AchievementFileParser.ParseFromDirectory(appDir).Where(a => a.IsUnlocked)];
			List<Achievement> newAchievements = DiffAndUpdate(key, currentUnlocked);

			if (newAchievements.Count == 0)
				return;

			SteamGameMetadata? metadata = await FetchMetadataAsync(key);

			foreach (Achievement ach in newAchievements)
			{
				EnrichAchievement(ach, metadata);
				RaiseAchievementUnlocked(key, ach);
			}
		}
		catch { }
	}

	private async Task<SteamGameMetadata?> FetchMetadataAsync(string appId)
	{
		string apiKey = settings.Settings.SteamApiKey;
		if (string.IsNullOrWhiteSpace(apiKey))
			return null;

		ApiKeyValidationResult validationResult = await steamApi.ValidateApiKeyAsync(apiKey);
		ApiKeyValidated?.Invoke(this, validationResult);

		if (validationResult != ApiKeyValidationResult.Valid)
			return null;

		try
		{
			return await steamApi.GetSteamGameMetadataAsync(appId);
		}
		catch
		{
			return null;
		}
	}

	private static void EnrichAchievement(
		Achievement ach,
		SteamGameMetadata? metadata)
	{
		if (metadata is null)
			return;

		if (!metadata.Achievements.TryGetValue(ach.Id, out SteamAchievementSchema? schema))
		{
			foreach ((string achKey, SteamAchievementSchema value) in metadata.Achievements)
			{
				if (achKey.Equals(ach.Id, StringComparison.OrdinalIgnoreCase))
				{
					schema = value;
					break;
				}
			}
		}

		if (schema is null)
			return;

		if (!string.IsNullOrEmpty(schema.Name))
			ach.Name = schema.Name;
		if (!string.IsNullOrEmpty(schema.Description))
			ach.Description = schema.Description;
		if (!string.IsNullOrEmpty(schema.IconUri))
			ach.IconUri = schema.IconUri;
		if (!string.IsNullOrEmpty(schema.IconLockedUri))
			ach.IconLockedUri = schema.IconLockedUri;
		ach.IsHidden = schema.IsHidden;
		ach.GlobalPercentage = schema.GlobalPercentage;
	}

	private string? FindAppDirectory(string appId)
	{
		foreach (WatchedFolderConfig folder in settings.Settings.WatchedFolders)
		{
			if (!folder.Enabled || string.IsNullOrWhiteSpace(folder.Path))
				continue;

			string resolved = SteamEmulatorScanner.ExpandPath(folder.Path);
			string candidate = Path.Combine(resolved, appId);
			if (Directory.Exists(candidate))
				return candidate;
		}
		return null;
	}

	private static string? FindAppIdDirectory(string filePath)
	{
		string? dir = Path.GetDirectoryName(filePath);
		while (dir is not null)
		{
			if (AchievementFileParser.IsAppId(Path.GetFileName(dir)))
				return dir;
			string? parent = Path.GetDirectoryName(dir);
			if (parent == dir)
				break;
			dir = parent;
		}
		return null;
	}
}
