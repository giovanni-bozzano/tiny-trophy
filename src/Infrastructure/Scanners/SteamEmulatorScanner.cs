using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure.Scanners;

/// <summary>
/// Scans Steam emulator directories for games with achievement data.
/// Each subdirectory is expected to be a Steam AppID containing achievement files.
/// </summary>
public sealed class SteamEmulatorScanner(ISettingsService settings)
	: IAchievementScanner
{
	public AchievementSource Source => AchievementSource.SteamEmulator;
	public string DisplayName => "Steam emulator folders";

	/// <summary>
	/// Maps each "well-known placeholder" token to its fixed location.
	/// </summary>
	private static readonly (string Token, string Value)[] s_pathTokens =
	[
		("%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
		("%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
		("%CommonDocuments%", Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)),
		("%ProgramFiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
		("%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
		("%ProgramData%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
		("%Documents%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
		("%UserProfile%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
		("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
	];

	/// <summary>
	/// Maps each "well-known placeholder" token to its fixed location relative to a Proton/Wine prefix root,
	/// mirroring the emulated Windows profile layout Wine/Proton always creates under a prefix.
	/// </summary>
	private static readonly (string Token, string Value)[] s_protonPrefixPathTokens =
	[
		("%AppData%", Path.Combine("drive_c", "users", "steamuser", "AppData", "Roaming")),
		("%LocalAppData%", Path.Combine("drive_c", "users", "steamuser", "AppData", "Local")),
		("%CommonDocuments%", Path.Combine("drive_c", "users", "Public", "Documents")),
		("%ProgramFiles(x86)%", Path.Combine("drive_c", "Program Files (x86)")),
		("%ProgramFiles%", Path.Combine("drive_c", "Program Files")),
		("%ProgramData%", Path.Combine("drive_c", "ProgramData")),
		("%Documents%", Path.Combine("drive_c", "users", "steamuser", "Documents")),
		("%UserProfile%", Path.Combine("drive_c", "users", "steamuser")),
	];

	/// <summary>
	/// Returns the default watched directories, populated on first run.
	/// Paths use placeholder tokens so they stay portable across user profiles.
	/// </summary>
	public static List<DirectoryConfig> GetDefaultWatchedDirectories()
	{
		return
		[
			new() { Path = Path.Combine(@"%AppData%", "Goldberg SteamEmu Saves"), Label = "Goldberg", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%AppData%", "GSE Saves"), Label = "GSE", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%CommonDocuments%", "OnlineFix"), Label = "OnlineFix", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%CommonDocuments%", "Steam", "RUNE"), Label = "RUNE", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%AppData%", "Steam", "CODEX"), Label = "CODEX (AppData)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%CommonDocuments%", "Steam", "CODEX"), Label = "CODEX (Public Documents)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%AppData%", "EMPRESS"), Label = "EMPRESS (AppData)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%CommonDocuments%", "EMPRESS"), Label = "EMPRESS (Public Documents)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%AppData%", "SmartSteamEmu"), Label = "SmartSteamEmu", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%LocalAppData%", "anadius", "LSX emu", "achievement_watcher"), Label = "Anadius LSX", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine(@"%LocalAppData%", "SKIDROW"), Label = "SKIDROW", Enabled = true, IsDefault = true },
		];
	}

	/// <summary>
	/// Returns the default Proton prefix directories, populated on first run on Linux. Each
	/// entry is a directory whose immediate subdirectories are individual Proton prefixes (i.e. each
	/// contains a "drive_c" folder), such as Heroic Games Launcher's default prefix location.
	/// </summary>
	public static List<DirectoryConfig> GetDefaultProtonPrefixDirectories()
	{
		return
		[
			new() { Path = Path.Combine("%UserProfile%", ".steam", "debian-installation", "steamapps", "compatdata"), Label = "Steam", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", ".local", "share", "Steam", "steamapps", "compatdata"), Label = "Steam (alternative)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", "Games", "Heroic", "Prefixes"), Label = "Heroic Games Launcher", Enabled = true, IsDefault = true },
		];
	}

	public Task<IReadOnlyList<Game>> ParseAsync(
		IProgress<(int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		List<Game> games = [];
		IReadOnlyList<string> watchedDirectories = GetEnabledWatchedDirectories(settings.Settings);
		IReadOnlyList<string> protonPrefixDirectories = GetEnabledProtonPrefixDirectories(settings.Settings);

		foreach (string watchedDirectory in watchedDirectories)
		{
			ct.ThrowIfCancellationRequested();

			foreach (string resolved in ExpandPathToAllCandidates(watchedDirectory, protonPrefixDirectories))
			{
				if (!Directory.Exists(resolved))
					continue;

				try
				{
					ScanFolder(resolved, games);
				}
				catch { }
			}
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
				FolderPath = appDir,
				Achievements = achievements
			});
		}
	}

	/// <summary>
	/// Returns the resolved paths of the user's enabled watched directories.
	/// </summary>
	public static IReadOnlyList<string> GetEnabledWatchedDirectories(AppSettings appSettings)
	{
		return [.. appSettings.WatchedDirectories
			.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.Path))
			.Select(d => ExpandPath(d.Path))];
	}

	/// <summary>
	/// Returns the resolved paths of the user's enabled Proton prefix directories.
	/// </summary>
	public static IReadOnlyList<string> GetEnabledProtonPrefixDirectories(AppSettings appSettings)
	{
		return [.. appSettings.ProtonPrefixDirectories
			.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.Path))
			.Select(d => ExpandPath(d.Path))];
	}

	/// <summary>
	/// Expands placeholder tokens in a path to their actual value.
	/// </summary>
	public static string ExpandPath(string path)
	{
		string resultingPath = path;
		foreach ((string? token, string? value) in s_pathTokens)
			resultingPath = resultingPath.Replace(token, value, StringComparison.OrdinalIgnoreCase);

		return resultingPath;
	}

	/// <summary>
	/// Replaces known environment placeholder tokens with their original values for portable storage.
	/// </summary>
	public static string CollapsePath(string path)
	{
		string resultingPath = path;
		foreach ((string? token, string? value) in s_pathTokens)
		{
			if (!string.IsNullOrEmpty(value))
				resultingPath = resultingPath.Replace(value, token, StringComparison.OrdinalIgnoreCase);
		}

		return resultingPath;
	}

	/// <summary>
	/// Expands one of the "well-known placeholder" tokens in <paramref name="watchedDirectoryPath"/> as if it were resolved
	/// inside a Proton/Wine prefix, i.e. against the prefix's emulated Windows user profile rather than
	/// the real Linux home directory used by <see cref="ExpandPath"/>.
	/// </summary>
	/// <remarks>
	/// Watched directory paths are always authored as Windows paths, since that is the layout Steam
	/// emulators use inside the Windows game they patch. Wine/Proton simulates the Windows user profile
	/// under each prefix at a fixed layout, so <see cref="s_protonPrefixTokenPaths"/> hardcodes that
	/// layout rather than looking it up dynamically - it describes a location inside the emulated
	/// Windows filesystem, not the real Linux one, so APIs like <see cref="Environment.GetFolderPath"/>
	/// don't apply here.
	/// </remarks>
	private static string? ExpandPathInProton(
		string watchedDirectoryPath,
		string protonPrefixRoot)
	{
		string? resultingPath = watchedDirectoryPath;
		foreach ((string token, string value) in s_protonPrefixPathTokens)
			resultingPath = resultingPath.Replace(token, value, StringComparison.OrdinalIgnoreCase);

		resultingPath = Path.Combine(protonPrefixRoot, resultingPath);

		return resultingPath;
	}

	/// <summary>
	/// Expands placeholder tokens in a path to every location it could plausibly resolve to.
	/// </summary>
	/// <remarks>
	/// On Windows this is just <see cref="ExpandPath"/>. On Linux, Windows games run under Proton
	/// or Wine each get their own prefix (a "drive_c" directory), so a Steam emulator's save path like
	/// "%AppData%\Goldberg SteamEmu Saves" lives under every game's prefix separately, rather than under
	/// one shared native directory. <paramref name="protonPrefixDirectories"/> lists the directories the user has
	/// configured as containers of such prefixes, and this returns one candidate per prefix found directly under
	/// each of those directories.
	/// </remarks>
	public static IReadOnlyList<string> ExpandPathToAllCandidates(
		string watchedDirectoryPath,
		IReadOnlyList<string> protonPrefixDirectories)
	{
		List<string> candidates = [ExpandPath(watchedDirectoryPath)];

		if (OperatingSystem.IsWindows())
			return candidates;

		// If on Linux, scan prefix directories for every Proton prefix, and expand the path under each prefix's "drive_c" folder.
		foreach (string protonPrefixDirectory in protonPrefixDirectories)
		{
			if (!Directory.Exists(protonPrefixDirectory))
				continue;

			// Probe all Proton prefixes in the Proton prefix directory
			foreach (string protonPrefixRoot in Directory.EnumerateDirectories(protonPrefixDirectory))
			{
				string? expanded = ExpandPathInProton(watchedDirectoryPath, protonPrefixRoot);
				if (expanded is not null)
					candidates.Add(expanded);
			}
		}

		return candidates;
	}
}
