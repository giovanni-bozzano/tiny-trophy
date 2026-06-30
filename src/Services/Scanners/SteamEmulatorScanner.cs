using TinyTrophy.Models;

namespace TinyTrophy.Services.Scanners;

/// <summary>
/// Scans Steam emulator folders for games with achievement data.
/// Each subfolder is expected to be a Steam AppID containing achievement files.
/// </summary>
public sealed class SteamEmulatorScanner(ISettingsService settings)
	: IAchievementScanner
{
	public AchievementSource Source => AchievementSource.SteamEmulator;
	public string DisplayName => "Steam emulator folders";

	public Task<IReadOnlyList<Game>> ParseAsync(
		IProgress<(int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		List<Game> games = [];
		List<WatchedFolderConfig> folders = settings.Settings.WatchedFolders;

		foreach (WatchedFolderConfig folder in folders)
		{
			ct.ThrowIfCancellationRequested();
			if (!folder.Enabled || string.IsNullOrWhiteSpace(folder.Path))
				continue;

			string resolved = ExpandPath(folder.Path);
			if (!Directory.Exists(resolved))
				continue;

			try
			{
				ScanFolder(resolved, games);
			}
			catch { }
		}

		return Task.FromResult<IReadOnlyList<Game>>(games);
	}

	private static void ScanFolder(
		string path,
		List<Game> games)
	{
		foreach (string appDir in Directory.EnumerateDirectories(path))
		{
			string appId = Path.GetFileName(appDir);

			if (!AchievementFileParser.IsAppId(appId))
				continue;

			List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(appDir);
			if (achievements.Count == 0)
				continue;

			games.Add(new Game
			{
				AppId = appId,
				Name = $"AppID: {appId}",
				Source = AchievementSource.SteamEmulator,
				Achievements = achievements
			});
		}
	}

	/// <summary>
	/// Returns the default watched folders, populated on first run.
	/// Paths use placeholder tokens so they stay portable across user profiles.
	/// </summary>
	public static List<WatchedFolderConfig> GetDefaultFolders()
	{
		return
		[
			new() { Path = @"%AppData%\Goldberg SteamEmu Saves", Label = "Goldberg", Enabled = true, IsDefault = true },
			new() { Path = @"%AppData%\GSE Saves", Label = "GSE", Enabled = true, IsDefault = true },
			new() { Path = @"%CommonDocuments%\OnlineFix", Label = "OnlineFix", Enabled = true, IsDefault = true },
			new() { Path = @"%CommonDocuments%\Steam\RUNE", Label = "RUNE", Enabled = true, IsDefault = true },
			new() { Path = @"%AppData%\Steam\CODEX", Label = "CODEX (AppData)", Enabled = true, IsDefault = true },
			new() { Path = @"%CommonDocuments%\Steam\CODEX", Label = "CODEX (Public Documents)", Enabled = true, IsDefault = true },
			new() { Path = @"%AppData%\EMPRESS", Label = "EMPRESS (AppData)", Enabled = true, IsDefault = true },
			new() { Path = @"%CommonDocuments%\EMPRESS", Label = "EMPRESS (Public Documents)", Enabled = true, IsDefault = true },
			new() { Path = @"%AppData%\SmartSteamEmu", Label = "SmartSteamEmu", Enabled = true, IsDefault = true },
			new() { Path = @"%LocalAppData%\anadius\LSX emu\achievement_watcher", Label = "Anadius LSX", Enabled = true, IsDefault = true },
			new() { Path = @"%LocalAppData%\SKIDROW", Label = "SKIDROW", Enabled = true, IsDefault = true },
		];
	}

	private static readonly (string Token, string Value)[] s_pathTokens =
	[
		("%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
		("%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
		("%CommonDocuments%", Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)),
		("%ProgramFiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
		("%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
		("%ProgramData%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
		("%UserProfile%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
		("%Documents%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
	];

	/// <summary>
	/// Expands placeholder tokens in a path to their actual values.
	/// </summary>
	public static string ExpandPath(string path)
	{
		foreach ((string? token, string? value) in s_pathTokens)
		{
			if (path.StartsWith(token, StringComparison.OrdinalIgnoreCase))
				return value + path[token.Length..];
		}

		return path;
	}

	/// <summary>
	/// Replaces known environment folder prefixes with placeholder tokens for portable storage.
	/// </summary>
	public static string CollapsePath(string path)
	{
		foreach ((string? token, string? value) in s_pathTokens)
		{
			if (path.StartsWith(value, StringComparison.OrdinalIgnoreCase))
				return token + path[value.Length..];
		}

		return path;
	}
}
