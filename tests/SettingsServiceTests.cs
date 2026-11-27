using System.Text.Json;
using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class SettingsServiceTests
{
	private static string SettingsPath(TempDirectory temp) => Path.Combine(temp.Root, "settings.json");

	[Fact]
	public async Task LoadAsync_WhenFileIsMissing_SeedsDefaultDirectories()
	{
		using TempDirectory temp = new();
		SettingsService service = new(SettingsPath(temp));

		await service.LoadAsync();

		Assert.NotEmpty(service.Settings.WatchedDirectories);
		Assert.All(service.Settings.WatchedDirectories, d => Assert.True(d.IsDefault));
		Assert.All(service.Settings.WatchedDirectories, d => Assert.True(d.Enabled));
		Assert.NotEmpty(service.Settings.ProtonPrefixDirectories);
	}

	[Fact]
	public async Task SaveAsync_ThenLoadAsync_RoundTripsValues()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);

		SettingsService service = new(path);
		await service.LoadAsync();
		service.Settings.SteamApiKey = "API-KEY";
		service.Settings.SteamId = "76561198000000000";
		service.Settings.Language = "english";
		service.Settings.Achievements.HideZeroPercent = false;
		service.Settings.Notifications.PlaySound = false;
		service.Settings.WatchedDirectories.Add(new DirectoryConfig
		{
			Path = Path.Combine(temp.Root, "custom-saves"),
			Label = "Custom",
			Enabled = true,
			IsDefault = false
		});
		await service.SaveAsync();

		SettingsService reloaded = new(path);
		await reloaded.LoadAsync();

		Assert.Equal("API-KEY", reloaded.Settings.SteamApiKey);
		Assert.Equal("76561198000000000", reloaded.Settings.SteamId);
		Assert.False(reloaded.Settings.Achievements.HideZeroPercent);
		Assert.False(reloaded.Settings.Notifications.PlaySound);

		DirectoryConfig custom = Assert.Single(reloaded.Settings.WatchedDirectories, d => !d.IsDefault);
		Assert.Equal("Custom", custom.Label);
		Assert.True(custom.Enabled);
	}

	[Fact]
	public async Task SaveAsync_DoesNotMutateLiveSettings()
	{
		using TempDirectory temp = new();
		string customPath = Path.Combine(temp.Root, "custom-saves");

		SettingsService service = new(SettingsPath(temp));
		await service.LoadAsync();
		service.Settings.WatchedDirectories.Add(new DirectoryConfig
		{
			Path = customPath,
			Label = "Custom",
			Enabled = true,
			IsDefault = false
		});

		await service.SaveAsync();

		DirectoryConfig live = Assert.Single(service.Settings.WatchedDirectories, d => !d.IsDefault);
		Assert.Equal(customPath, live.Path);
	}

	[Fact]
	public async Task SaveAsync_PersistsOnlyCustomAndDisabledDefaultDirectories()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);

		SettingsService service = new(path);
		await service.LoadAsync();

		// All defaults are enabled and no custom folders exist yet
		await service.SaveAsync();
		using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
		{
			Assert.Equal(0, document.RootElement.GetProperty("WatchedDirectories").GetArrayLength());
		}

		// Disabling a default persists it, so the disabled state survives a reload
		service.Settings.WatchedDirectories[0].Enabled = false;
		await service.SaveAsync();
		using (JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
		{
			Assert.Equal(1, document.RootElement.GetProperty("WatchedDirectories").GetArrayLength());
		}
	}

	[Fact]
	public async Task LoadAsync_RestoresDefaultsAndKeepsDisabledAndCustomDirectories()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);

		List<DirectoryConfig> defaults = SteamEmulatorScanner.GetDefaultWatchedDirectories();
		DirectoryConfig disabledDefault = defaults[0];
		disabledDefault.Enabled = false;

		AppSettings saved = new()
		{
			WatchedDirectories =
			[
				new DirectoryConfig
				{
					Path = disabledDefault.Path,
					Label = disabledDefault.Label,
					Enabled = false,
					IsDefault = true
				},
				new DirectoryConfig
				{
					Path = Path.Combine(temp.Root, "custom-saves"),
					Label = "Custom",
					Enabled = true,
					IsDefault = false
				}
			]
		};
		await File.WriteAllTextAsync(
			path,
			JsonSerializer.Serialize(saved, AppJsonContext.Default.AppSettings));

		SettingsService service = new(path);
		await service.LoadAsync();

		Assert.Equal(defaults.Count + 1, service.Settings.WatchedDirectories.Count);

		DirectoryConfig restored = Assert.Single(
			service.Settings.WatchedDirectories,
			d => d.IsDefault && d.Path == disabledDefault.Path);
		Assert.False(restored.Enabled);

		Assert.All(
			service.Settings.WatchedDirectories.Where(d => d.IsDefault && d.Path != disabledDefault.Path),
			d => Assert.True(d.Enabled));

		DirectoryConfig custom = Assert.Single(service.Settings.WatchedDirectories, d => !d.IsDefault);
		Assert.Equal("Custom", custom.Label);
		Assert.True(custom.Enabled);

		// Folder defaults are re-added on every load, so a new version's defaults appear too
		Assert.NotEmpty(service.Settings.ProtonPrefixDirectories);
		Assert.All(service.Settings.ProtonPrefixDirectories, d => Assert.True(d.IsDefault));
	}

	[Fact]
	public async Task SaveAsync_LeavesNoTemporaryFileBehind()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);

		SettingsService service = new(path);
		await service.LoadAsync();
		await service.SaveAsync();

		Assert.True(File.Exists(path));
		Assert.False(File.Exists(path + ".tmp"));
		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
		Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
	}

	[Fact]
	public async Task LoadAsync_WithCorruptFile_MovesItAsideAndUsesDefaults()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);
		await File.WriteAllTextAsync(path, "{ this is not json");

		SettingsService service = new(path);
		await service.LoadAsync();

		Assert.NotEmpty(service.Settings.WatchedDirectories);
		Assert.True(File.Exists(path + ".corrupt"));
		Assert.Contains("this is not json", await File.ReadAllTextAsync(path + ".corrupt"));
		Assert.False(File.Exists(path));
	}

	[Fact]
	public async Task LoadAsync_WithRepeatedCorruptFiles_KeepsEveryBackup()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);

		await File.WriteAllTextAsync(path, "{ first");
		SettingsService first = new(path);
		await first.LoadAsync();

		await File.WriteAllTextAsync(path, "{ second");
		SettingsService second = new(path);
		await second.LoadAsync();

		string[] backups = Directory.GetFiles(temp.Root, "settings.json.corrupt*");
		Assert.Equal(2, backups.Length);
		Assert.Contains(backups, file => File.ReadAllText(file) == "{ second");
	}

	[Fact]
	public async Task LoadAsync_WhenTheFileIsTemporarilyLocked_LeavesItInPlace()
	{
		// FileShare.None is enforced by Windows; Unix does not reliably block a second open this way.
		if (!OperatingSystem.IsWindows())
			return;

		using TempDirectory temp = new();
		string path = SettingsPath(temp);
		await File.WriteAllTextAsync(path, """{ "SteamApiKey": "KEY" }""");

		using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			SettingsService service = new(path);
			await service.LoadAsync();

			Assert.True(File.Exists(path));
			Assert.False(File.Exists(path + ".corrupt"));
			Assert.NotEmpty(service.Settings.WatchedDirectories);
		}
	}

	[Fact]
	public async Task SaveAsync_ConcurrentCalls_ProduceReadableSettings()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);

		SettingsService service = new(path);
		await service.LoadAsync();

		await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(service.SaveAsync)));

		SettingsService reloaded = new(path);
		await reloaded.LoadAsync();

		Assert.NotEmpty(reloaded.Settings.WatchedDirectories);
		Assert.False(File.Exists(path + ".tmp"));
	}

	[Fact]
	public async Task LoadAsync_WithPartialJson_KeepsDefaultsForMissingFields()
	{
		using TempDirectory temp = new();
		string path = SettingsPath(temp);
		await File.WriteAllTextAsync(path, """{ "SteamApiKey": "KEY" }""");

		SettingsService service = new(path);
		await service.LoadAsync();

		Assert.Equal("KEY", service.Settings.SteamApiKey);
		Assert.True(service.Settings.Achievements.MergeDuplicate);
		Assert.True(service.Settings.Achievements.HideZeroPercent);
		Assert.Equal("english", service.Settings.Language);
		Assert.True(service.Settings.CloseToTray);
		Assert.NotEmpty(service.Settings.WatchedDirectories);
	}
}
