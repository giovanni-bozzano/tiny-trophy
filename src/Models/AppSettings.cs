namespace TinyTrophy.Models;

public sealed class AppSettings
{
	public AchievementsSettings Achievements { get; set; } = new();
	public List<WatchedFolderConfig> WatchedFolders { get; set; } = [];
	public NotificationSettings Notifications { get; set; } = new();
	public SortSettings HomeSort { get; set; } = new();
	public SortSettings GameDetailSort { get; set; } = new();
	public string SteamApiKey { get; set; } = string.Empty;
	public string SteamId { get; set; } = string.Empty;
	public string Language { get; set; } = "english";
	public bool SteamOfficialEnabled { get; set; } = true;
	public bool ShadPs4Enabled { get; set; } = true;
	public bool StartMinimized { get; set; }
	public bool CheckForUpdates { get; set; } = true;
}

public sealed class AchievementsSettings
{
	public bool ShowHidden { get; set; }
	public bool MergeDuplicate { get; set; } = true;
	public bool HideZeroPercent { get; set; } = true;
}

/// <summary>
/// Represents a single watched folder source. Each folder is individually toggleable.
/// Default folders are auto-populated on first run and can be disabled but not removed.
/// Custom folders can be added and removed by the user.
/// </summary>
public sealed class WatchedFolderConfig
{
	public string Path { get; set; } = string.Empty;
	public string Label { get; set; } = string.Empty;
	public bool Enabled { get; set; } = true;
	public bool IsDefault { get; set; }
}

public sealed class NotificationSettings
{
	public bool Enabled { get; set; } = true;
	public bool PlaySound { get; set; } = true;
}

public sealed class SortSettings
{
	public string Mode { get; set; } = string.Empty;
	public bool Ascending { get; set; } = true;
}
