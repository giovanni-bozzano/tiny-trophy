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
	private static readonly string SettingsFile = Path.Combine(AppPaths.DataDir, "settings.json");

	public AppSettings Settings { get; private set; } = new();

	public async Task LoadAsync()
	{
		if (!File.Exists(SettingsFile))
		{
			Settings = new AppSettings
			{
				WatchedDirectories = SteamEmulatorScanner.GetDefaultWatchedDirectories(),
				ProtonPrefixDirectories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories()
			};
			return;
		}

		try
		{
			string json = File.ReadAllText(SettingsFile);
			Settings = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppSettings) ?? new AppSettings();
		}
		catch
		{
			Settings = new AppSettings();
		}

		// Merge hard-coded default watched directories with user customizations
		List<DirectoryConfig> defaultWatchedDirectories = SteamEmulatorScanner.GetDefaultWatchedDirectories();
		List<DirectoryConfig> savedWatchedDirectories = Settings.WatchedDirectories;

		// Restore the user's disabled state for default watched directories
		HashSet<string> disabledWatchedDirectories = savedWatchedDirectories
			.Where(d => d.IsDefault && !d.Enabled)
			.Select(d => d.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (DirectoryConfig watchedDirectory in defaultWatchedDirectories)
		{
			if (disabledWatchedDirectories.Contains(watchedDirectory.Path))
				watchedDirectory.Enabled = false;
		}

		List<DirectoryConfig> customFolders = [.. savedWatchedDirectories.Where(d => !d.IsDefault)];
		Settings.WatchedDirectories = [.. defaultWatchedDirectories, .. customFolders];

		// Merge hard-coded default Proton prefix directories with user customizations
		List<DirectoryConfig> defaultProtonPrefixDirectories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories();
		List<DirectoryConfig> savedProtonPrefixDirectories = Settings.ProtonPrefixDirectories;

		// Restore the user's disabled state for default Proton prefix directories
		HashSet<string> disabledProtonPrefixDirectories = savedProtonPrefixDirectories
			.Where(d => d.IsDefault && !d.Enabled)
			.Select(d => d.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

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
		Directory.CreateDirectory(AppPaths.DataDir);

		// Only save custom folders (with portable paths) and explicitly disabled defaults
		List<DirectoryConfig> originalWatchedDirectories = Settings.WatchedDirectories;
		HashSet<string> currentDefaultWatchedDirectories = SteamEmulatorScanner.GetDefaultWatchedDirectories()
			.Select(d => d.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Settings.WatchedDirectories = [.. originalWatchedDirectories
			.Where(d => !d.IsDefault || !d.Enabled && currentDefaultWatchedDirectories.Contains(d.Path))
			.Select(d => d.IsDefault ? d : new DirectoryConfig
			{
				Path = SteamEmulatorScanner.CollapsePath(d.Path),
				Label = d.Label,
				Enabled = d.Enabled,
				IsDefault = false
			})];

		// Only save custom folders (with portable paths) and explicitly disabled defaults
		List<DirectoryConfig> originalProtonPrefixDirectories = Settings.ProtonPrefixDirectories;
		HashSet<string> currentDefaultProtonPrefixDirectories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories()
			.Select(d => d.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Settings.ProtonPrefixDirectories = [.. originalProtonPrefixDirectories
			.Where(d => !d.IsDefault || !d.Enabled && currentDefaultProtonPrefixDirectories.Contains(d.Path))
			.Select(d => d.IsDefault ? d : new DirectoryConfig
			{
				Path = SteamEmulatorScanner.CollapsePath(d.Path),
				Label = d.Label,
				Enabled = d.Enabled,
				IsDefault = false
			})];

		string json = JsonSerializer.Serialize(Settings, AppJsonContext.Default.AppSettings);
		await File.WriteAllTextAsync(SettingsFile, json);

		Settings.WatchedDirectories = originalWatchedDirectories;
		Settings.ProtonPrefixDirectories = originalProtonPrefixDirectories;

		SteamEmulatorScanner.ClearExpandedPathCache();
	}
}
