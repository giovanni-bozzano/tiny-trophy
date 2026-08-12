namespace TinyTrophy.Infrastructure;

/// <summary>
/// Central place resolving the OS-appropriate root directories the app uses for cached data (safe to
/// delete, e.g. downloaded images/API responses) and persisted app data (settings, flags).
/// </summary>
/// <remarks>
/// On Windows both roots point under %LocalAppData%/%AppData% respectively, matching prior behaviour.
/// On Linux, <see cref="Environment.SpecialFolder.LocalApplicationData"/> and
/// <see cref="Environment.SpecialFolder.ApplicationData"/> both resolve from $HOME and can come back
/// empty in some launch contexts (e.g. desktop entries, services) where $HOME isn't set, which would
/// silently make paths relative to the process's launch directory. XDG base directories are resolved
/// explicitly instead, per the XDG Base Directory Specification.
/// </remarks>
public static class AppPaths
{
	private const string AppName = "tinytrophy";

	/// <summary>
	/// Root directory for cached, disposable data (e.g. downloaded images, API response cache).
	/// </summary>
	public static string CacheDir { get; } = Path.Combine(GetCacheRootDir(), AppName);

	/// <summary>
	/// OS-appropriate root directory persisted app data lives under, without the app-specific subfolder
	/// (e.g. XDG_CONFIG_HOME on Linux, %AppData% on Windows). Used for locations like autostart entries
	/// that live alongside, rather than inside, <see cref="DataDir"/>.
	/// </summary>
	public static string DataRootDir { get; } = GetDataRootDir();

	/// <summary>
	/// Root directory for persisted app data (e.g. settings, update flags).
	/// </summary>
	public static string DataDir { get; } = Path.Combine(DataRootDir, AppName);

	private static string GetCacheRootDir()
	{
		if (OperatingSystem.IsLinux())
		{
			string? xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
			if (!string.IsNullOrWhiteSpace(xdgCacheHome))
				return xdgCacheHome;

			string? home = Environment.GetEnvironmentVariable("HOME");
			if (!string.IsNullOrWhiteSpace(home))
				return Path.Combine(home, ".cache");
		}

		return GetFolderPathOrThrow(Environment.SpecialFolder.LocalApplicationData);
	}

	private static string GetDataRootDir()
	{
		if (OperatingSystem.IsLinux())
		{
			string? xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
			if (!string.IsNullOrWhiteSpace(xdgConfigHome))
				return xdgConfigHome;

			string? home = Environment.GetEnvironmentVariable("HOME");
			if (!string.IsNullOrWhiteSpace(home))
				return Path.Combine(home, ".config");
		}

		return GetFolderPathOrThrow(Environment.SpecialFolder.ApplicationData);
	}

	private static string GetFolderPathOrThrow(Environment.SpecialFolder folder)
	{
		string path = Environment.GetFolderPath(folder);
		if (!string.IsNullOrWhiteSpace(path))
			return path;

		// Environment.GetFolderPath can resolve to an empty string on Linux when HOME isn't set, which
		// would silently make callers cache/save data relative to the process's launch directory. Fail
		// loudly instead.
		throw new InvalidOperationException($"Could not determine the '{folder}' special folder path.");
	}
}
