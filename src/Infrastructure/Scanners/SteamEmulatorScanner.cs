using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure.Scanners;

/// <summary>
/// Scans Steam emulator directories for games with achievement data.
/// Each subdirectory is expected to be a Steam AppID containing achievement files.
/// </summary>
public sealed class SteamEmulatorScanner(ISettingsService settings)
	: ISingleGameScanner
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
	/// Returns the default Proton prefix directories, populated on first run on Linux. Each entry is the
	/// full path to a directory that directly contains "drive_c", with "*" segments meaning "any
	/// directory at this level" (see <see cref="ExpandProtonPrefixDirectoryGlob"/>).
	/// </summary>
	public static List<DirectoryConfig> GetDefaultProtonPrefixDirectories()
	{
		return
		[
			new() { Path = Path.Combine("%UserProfile%", ".steam", "steam", "steamapps", "compatdata", "*", "pfx"), Label = "Steam", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", ".local", "share", "Steam", "steamapps", "compatdata", "*", "pfx"), Label = "Steam (alternative)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", ".var", "app", "com.valvesoftware.Steam", ".steam", "steam", "steamapps", "compatdata", "*", "pfx"), Label = "Steam (Flatpak)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps", "compatdata", "*", "pfx"), Label = "Steam (Flatpak alternative)", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", "Games", "Heroic", "Prefixes", "*"), Label = "Heroic Games Launcher", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", "Games", "*", "*"), Label = "Lutris", Enabled = true, IsDefault = true },
			new() { Path = Path.Combine("%UserProfile%", ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles", "*"), Label = "Bottles (Flatpak)", Enabled = true, IsDefault = true },
		];
	}

	/// <summary>
	/// Caches the result of <see cref="ResolveDirectories"/>, computed from the user's enabled watched
	/// directories and Proton prefix directories. Re-doing this whole pipeline (including re-globbing
	/// Proton prefixes on disk) on every scan/watch setup call is wasteful since the settings don't change
	/// between calls. Cleared via <see cref="ClearResolvedDirectoriesCache"/> whenever settings are saved
	/// or paths are rescanned.
	/// </summary>
	private static ResolvedDirectories? s_resolvedDirectoriesCache;

	public Task<IReadOnlyList<Game>> ParseAsync(
		IProgress<(int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		List<Game> games = [];
		IReadOnlyList<string> resolvedDirectories = GetEnabledResolvedDirectories(settings.Settings);

		foreach (string resolved in resolvedDirectories)
		{
			ct.ThrowIfCancellationRequested();

			try
			{
				ScanFolder(resolved, games);
			}
			catch { }
		}

		return Task.FromResult<IReadOnlyList<Game>>(games);
	}

	public Task<IReadOnlyList<Game>> ParseGameAsync(
		string gameId,
		CancellationToken ct = default)
	{
		List<Game> games = [];
		if (!AchievementFileParser.IsAppId(gameId))
			return Task.FromResult<IReadOnlyList<Game>>(games);

		foreach (string resolved in GetEnabledResolvedDirectories(settings.Settings))
		{
			ct.ThrowIfCancellationRequested();

			try
			{
				string appDir = Path.Combine(resolved, gameId);
				if (Directory.Exists(appDir) && TryParseGame(appDir) is Game game)
					games.Add(game);
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
			if (!AchievementFileParser.IsAppId(Path.GetFileName(appDir)))
				continue;

			if (TryParseGame(appDir) is Game game)
				games.Add(game);
		}
	}

	private static Game? TryParseGame(string appDir)
	{
		string appId = Path.GetFileName(appDir);

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(appDir);
		if (achievements.Count == 0)
			return null;

		return new Game
		{
			AppId = appId,
			Name = $"AppID: {appId}",
			Source = AchievementSource.SteamEmulator,
			FolderPath = appDir,
			Achievements = achievements
		};
	}

	/// <summary>
	/// Returns the resolved patterns of the user's enabled Proton prefix directories. Each pattern is
	/// the full path to a directory that directly contains "drive_c", with "*" segments meaning "any
	/// directory at this level" (see <see cref="ExpandProtonPrefixDirectoryGlob"/>).
	/// </summary>
	private static IReadOnlyList<string> GetEnabledProtonPrefixDirectories(AppSettings appSettings)
	{
		return [.. appSettings.ProtonPrefixDirectories
			.Where(d => d.Enabled && !string.IsNullOrWhiteSpace(d.Path))
			.Select(d => ExpandPath(d.Path))
			.OfType<string>()];
	}

	/// <summary>
	/// Expands placeholder tokens in a path to their actual value, or <see langword="null"/> if the path
	/// references a token that has no value on the current OS (e.g. a Windows-only special folder while
	/// running on Linux), meaning the path can't be resolved at all.
	/// </summary>
	public static string? ExpandPath(string path)
	{
		string resultingPath = path;
		foreach ((string token, string value) in s_pathTokens)
		{
			if (!resultingPath.Contains(token, StringComparison.OrdinalIgnoreCase))
				continue;

			if (string.IsNullOrEmpty(value))
				return null;

			resultingPath = resultingPath.Replace(token, value, StringComparison.OrdinalIgnoreCase);
		}

		return resultingPath;
	}

	/// <summary>
	/// Replaces known environment placeholder tokens with their original values for portable storage.
	/// </summary>
	public static string CollapsePath(string path)
	{
		// Real paths are case-insensitive on Windows only
		StringComparison pathComparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		string resultingPath = path;
		foreach ((string? token, string? value) in s_pathTokens)
		{
			if (!string.IsNullOrEmpty(value))
				resultingPath = resultingPath.Replace(value, token, pathComparison);
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
	private static string ExpandPathInProton(
		string watchedDirectoryPath,
		string protonPrefixRoot)
	{
		string resultingPath = watchedDirectoryPath;
		foreach ((string token, string value) in s_protonPrefixPathTokens)
			resultingPath = resultingPath.Replace(token, value, StringComparison.OrdinalIgnoreCase);

		return Path.Combine(protonPrefixRoot, resultingPath);
	}

	/// <summary>
	/// Expands a "*" glob segment in a Proton prefix directory pattern into every real, non-symlink
	/// directory that matches, e.g. ".../compatdata/*/pfx" resolves "*" against every appid directory
	/// under "compatdata" and returns each matching "pfx" directory found to actually exist.
	/// </summary>
	/// <remarks>
	/// Symlinks are skipped because compatibility tools sometimes symlink one prefix onto another (e.g.
	/// to share a prefix between two app IDs), which would otherwise report the same real directory as
	/// two separate candidates.
	/// </remarks>
	private static IEnumerable<string> ExpandProtonPrefixDirectoryGlob(string pattern)
	{
		string[] segments = pattern.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		IEnumerable<string> candidatePaths = [segments[0]];

		if (pattern.StartsWith(Path.DirectorySeparatorChar) || pattern.StartsWith(Path.AltDirectorySeparatorChar))
			candidatePaths = [Path.DirectorySeparatorChar.ToString()];

		foreach (string segment in segments)
		{
			if (segment != "*")
			{
				candidatePaths = candidatePaths.Select(candidatePath => Path.Combine(candidatePath, segment));
				continue;
			}

			candidatePaths = candidatePaths.SelectMany(candidatePath =>
			{
				if (!Directory.Exists(candidatePath))
					return [];

				IEnumerable<string> nonSymlinks = Directory.EnumerateDirectories(candidatePath)
					.Where(childPath => !new DirectoryInfo(childPath).Attributes.HasFlag(FileAttributes.ReparsePoint));
				return nonSymlinks;
			});
		}

		return candidatePaths.Where(Directory.Exists);
	}

	/// <summary>
	/// Clears <see cref="s_resolvedDirectoriesCache"/>, so the next lookup re-resolves the directories
	/// against the current configuration and filesystem.
	/// </summary>
	public static void ClearResolvedDirectoriesCache()
	{
		s_resolvedDirectoriesCache = null;
	}

	/// <summary>
	/// Returns every directory that exists on disk across all of the user's enabled watched directories,
	/// expanded to every plausible candidate location (including Proton prefix candidates on Linux).
	/// </summary>
	public static IReadOnlyList<string> GetEnabledResolvedDirectories(AppSettings appSettings)
	{
		return ResolveDirectories(appSettings).Directories;
	}

	/// <summary>
	/// Returns, for every enabled watched directory, every candidate path it expanded to alongside
	/// whether it was detected, exactly as currently used by the app for scanning and watching.
	/// Intended for the debug diagnostics panel in Settings.
	/// </summary>
	public static IReadOnlyList<WatchedDirectoryDebugInfo> GetWatchedDirectoryDebugInfo(AppSettings appSettings)
	{
		return ResolveDirectories(appSettings).DebugInfo;
	}

	private static ResolvedDirectories ResolveDirectories(AppSettings appSettings)
	{
		if (s_resolvedDirectoriesCache is not null)
			return s_resolvedDirectoriesCache;

		// Glob the Proton prefixes once, rather than once per watched directory
		IReadOnlyList<string> protonPrefixRoots = OperatingSystem.IsWindows()
			? []
			: [.. GetEnabledProtonPrefixDirectories(appSettings).SelectMany(ExpandProtonPrefixDirectoryGlob)];

		List<string> directories = [];
		List<WatchedDirectoryDebugInfo> debugInfo = [];
		foreach (DirectoryConfig watchedDirectory in appSettings.WatchedDirectories)
		{
			if (!watchedDirectory.Enabled || string.IsNullOrWhiteSpace(watchedDirectory.Path))
				continue;

			List<WatchedDirectoryCandidate> candidates = [];
			foreach (string candidate in ExpandPathToAllCandidates(watchedDirectory.Path, protonPrefixRoots))
			{
				bool exists = Directory.Exists(candidate);
				if (exists)
					directories.Add(candidate);
				candidates.Add(new WatchedDirectoryCandidate(candidate, exists));
			}

			debugInfo.Add(new WatchedDirectoryDebugInfo(watchedDirectory.Label, watchedDirectory.Path, candidates));
		}

		return s_resolvedDirectoriesCache = new ResolvedDirectories(directories, debugInfo);
	}

	/// <summary>
	/// Expands placeholder tokens in a path to every location it could plausibly resolve to.
	/// </summary>
	/// <remarks>
	/// On Windows this is just <see cref="ExpandPath"/>. On Linux, Windows games run under Proton
	/// or Wine each get their own prefix (a "drive_c" directory), so a Steam emulator's save path like
	/// "%AppData%\Goldberg SteamEmu Saves" lives under every game's prefix separately, rather than under
	/// one shared native directory. <paramref name="protonPrefixRoots"/> lists the already globbed
	/// directories directly containing "drive_c", and this returns one candidate per prefix.
	/// </remarks>
	private static List<string> ExpandPathToAllCandidates(
		string watchedDirectoryPath,
		IReadOnlyList<string> protonPrefixRoots)
	{
		List<string> candidates = [];

		string? nativeCandidate = ExpandPath(watchedDirectoryPath);
		if (nativeCandidate is not null)
			candidates.Add(nativeCandidate);

		foreach (string protonPrefixRoot in protonPrefixRoots)
			candidates.Add(ExpandPathInProton(watchedDirectoryPath, protonPrefixRoot));

		return candidates;
	}

	private sealed record ResolvedDirectories(
		IReadOnlyList<string> Directories,
		IReadOnlyList<WatchedDirectoryDebugInfo> DebugInfo);
}

/// <summary>
/// Debug diagnostics for a single watched directory entry: its raw configured path and every candidate
/// location it was expanded to.
/// </summary>
public sealed record WatchedDirectoryDebugInfo(
	string Label,
	string RawPath,
	IReadOnlyList<WatchedDirectoryCandidate> Candidates);

/// <summary>
/// A single expanded candidate path and whether it exists on disk.
/// </summary>
public sealed record WatchedDirectoryCandidate(
	string Path,
	bool Exists);
