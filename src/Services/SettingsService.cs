using System.Text.Json;
using TinyTrophy.Models;
using TinyTrophy.Services.Scanners;

namespace TinyTrophy.Services;

public interface ISettingsService
{
	AppSettings Settings { get; }
	Task LoadAsync();
	Task SaveAsync();
}

public sealed class SettingsService
	: ISettingsService
{
	private static readonly string SettingsDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"TinyTrophy");

	private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

	public AppSettings Settings { get; private set; } = new();

	public async Task LoadAsync()
	{
		if (!File.Exists(SettingsFile))
		{
			Settings = new AppSettings { WatchedFolders = SteamEmulatorScanner.GetDefaultFolders() };
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

		// Merge hard-coded default folders with user customizations
		List<WatchedFolderConfig> defaults = SteamEmulatorScanner.GetDefaultFolders();
		List<WatchedFolderConfig> savedFolders = Settings.WatchedFolders;

		// Restore the user's disabled state for default folders
		HashSet<string> disabledPaths = savedFolders
			.Where(f => f.IsDefault && !f.Enabled)
			.Select(f => f.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (WatchedFolderConfig folder in defaults)
		{
			if (disabledPaths.Contains(folder.Path))
				folder.Enabled = false;
		}

		List<WatchedFolderConfig> customFolders = [.. savedFolders.Where(f => !f.IsDefault)];
		Settings.WatchedFolders = [.. defaults, .. customFolders];
	}

	public async Task SaveAsync()
	{
		Directory.CreateDirectory(SettingsDir);

		// Only save custom folders (with portable paths) and explicitly disabled defaults
		List<WatchedFolderConfig> original = Settings.WatchedFolders;
		HashSet<string> currentDefaultPaths = SteamEmulatorScanner.GetDefaultFolders()
			.Select(f => f.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Settings.WatchedFolders = [.. original
			.Where(f => !f.IsDefault || !f.Enabled && currentDefaultPaths.Contains(f.Path))
			.Select(f => f.IsDefault ? f : new WatchedFolderConfig
			{
				Path = SteamEmulatorScanner.CollapsePath(f.Path),
				Label = f.Label,
				Enabled = f.Enabled,
				IsDefault = false
			})];

		string json = JsonSerializer.Serialize(Settings, AppJsonContext.Default.AppSettings);
		await File.WriteAllTextAsync(SettingsFile, json);

		Settings.WatchedFolders = original;
	}
}
