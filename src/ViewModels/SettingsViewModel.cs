using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.ViewModels;

public sealed partial class SettingsViewModel
	: ObservableObject
{
	private readonly ISettingsService _settingsService;
	private readonly MainViewModel _mainViewModel;
	private readonly Func<Task>? _checkForUpdate;

	[ObservableProperty]
	public partial string SteamApiKey { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SteamId { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Language { get; set; } = "english";

	[ObservableProperty]
	public partial bool ShowHidden { get; set; }

	[ObservableProperty]
	public partial bool MergeDuplicates { get; set; } = true;

	[ObservableProperty]
	public partial bool HideZeroPercent { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<DirectoryItemViewModel> WatchedDirectories { get; set; } = [];

	[ObservableProperty]
	public partial string NewWatchedDirectoryPath { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string NewWatchedDirectoryLabel { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<DirectoryItemViewModel> ProtonPrefixDirectories { get; set; } = [];

	[ObservableProperty]
	public partial string NewProtonPrefixDirectoryPath { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string NewProtonPrefixDirectoryLabel { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool NotificationsEnabled { get; set; } = true;

	[ObservableProperty]
	public partial bool NotificationSound { get; set; } = true;

	[ObservableProperty]
	public partial bool CheckForUpdates { get; set; } = true;

	[ObservableProperty]
	public partial bool SteamOfficialEnabled { get; set; } = true;

	[ObservableProperty]
	public partial bool ShadPs4Enabled { get; set; } = true;

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = string.Empty;

	public bool IsLinux { get; } = OperatingSystem.IsLinux();

	public string[] AvailableLanguages { get; } = ["english"];

	public SettingsViewModel(
		ISettingsService settingsService,
		MainViewModel mainViewModel,
		Func<Task>? checkForUpdate = null)
	{
		_settingsService = settingsService;
		_mainViewModel = mainViewModel;
		_checkForUpdate = checkForUpdate;
		LoadFromSettings();
	}

	private string _originalApiKey = string.Empty;

	public void LoadFromSettings()
	{
		AppSettings s = _settingsService.Settings;
		SteamApiKey = s.SteamApiKey;
		_originalApiKey = s.SteamApiKey;
		SteamId = s.SteamId;
		Language = s.Language;
		ShowHidden = s.Achievements.ShowHidden;
		MergeDuplicates = s.Achievements.MergeDuplicate;
		HideZeroPercent = s.Achievements.HideZeroPercent;
		NotificationsEnabled = s.Notifications.Enabled;
		NotificationSound = s.Notifications.PlaySound;
		CheckForUpdates = s.CheckForUpdates;
		SteamOfficialEnabled = s.SteamOfficialEnabled;
		ShadPs4Enabled = s.ShadPs4Enabled;

		WatchedDirectories = new ObservableCollection<DirectoryItemViewModel>(s.WatchedDirectories.Select(d => new DirectoryItemViewModel(d)));
		ProtonPrefixDirectories = new ObservableCollection<DirectoryItemViewModel>(s.ProtonPrefixDirectories.Select(d => new DirectoryItemViewModel(d)));
	}

	[RelayCommand]
	private void AddWatchedDirectory()
	{
		string path = NewWatchedDirectoryPath.Trim();
		if (string.IsNullOrWhiteSpace(path))
			return;

		string label = string.IsNullOrWhiteSpace(NewWatchedDirectoryLabel)
			? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
			: NewWatchedDirectoryLabel.Trim();

		WatchedDirectories.Add(new DirectoryItemViewModel(new DirectoryConfig
		{
			Path = path,
			Label = label,
			Enabled = true,
			IsDefault = false
		}));

		NewWatchedDirectoryPath = string.Empty;
		NewWatchedDirectoryLabel = string.Empty;
	}

	[RelayCommand]
	private void RemoveWatchedDirectory(DirectoryItemViewModel? directory)
	{
		if (directory is null || directory.IsDefault)
			return;

		WatchedDirectories.Remove(directory);
	}

	[RelayCommand]
	private void AddProtonPrefixDirectory()
	{
		string path = NewProtonPrefixDirectoryPath.Trim();
		if (string.IsNullOrWhiteSpace(path))
			return;

		string label = string.IsNullOrWhiteSpace(NewProtonPrefixDirectoryLabel)
			? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
			: NewProtonPrefixDirectoryLabel.Trim();

		ProtonPrefixDirectories.Add(new DirectoryItemViewModel(new DirectoryConfig
		{
			Path = path,
			Label = label,
			Enabled = true,
			IsDefault = false
		}));

		NewProtonPrefixDirectoryPath = string.Empty;
		NewProtonPrefixDirectoryLabel = string.Empty;
	}

	[RelayCommand]
	private void RemoveProtonPrefixDirectory(DirectoryItemViewModel? directory)
	{
		if (directory is null || directory.IsDefault)
			return;

		ProtonPrefixDirectories.Remove(directory);
	}

	[RelayCommand]
	private async Task SaveSettingsAsync()
	{
		StatusMessage = string.Empty;
		AppSettings s = _settingsService.Settings;
		s.SteamApiKey = SteamApiKey;
		s.SteamId = SteamId;
		s.Language = Language;
		s.Achievements.ShowHidden = ShowHidden;
		s.Achievements.MergeDuplicate = MergeDuplicates;
		s.Achievements.HideZeroPercent = HideZeroPercent;
		s.Notifications.Enabled = NotificationsEnabled;
		s.Notifications.PlaySound = NotificationSound;
		s.CheckForUpdates = CheckForUpdates;
		s.SteamOfficialEnabled = SteamOfficialEnabled;
		s.ShadPs4Enabled = ShadPs4Enabled;

		s.WatchedDirectories = [.. WatchedDirectories.Select(d => d.ToConfig())];
		s.ProtonPrefixDirectories = [.. ProtonPrefixDirectories.Select(d => d.ToConfig())];

		await _settingsService.SaveAsync();

		if (!string.Equals(SteamApiKey, _originalApiKey, StringComparison.Ordinal))
		{
			_originalApiKey = SteamApiKey;
			await _mainViewModel.RefreshMetadataCommand.ExecuteAsync(null);
		}
		else
		{
			await _mainViewModel.ReloadWatchedFoldersAsync();
		}
	}

	[RelayCommand]
	private void ResetDefaults()
	{
		AppSettings defaults = new()
		{
			WatchedDirectories = SteamEmulatorScanner.GetDefaultWatchedDirectories(),
			ProtonPrefixDirectories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories()
		};

		SteamApiKey = string.Empty;
		SteamId = string.Empty;
		Language = defaults.Language;
		ShowHidden = defaults.Achievements.ShowHidden;
		MergeDuplicates = defaults.Achievements.MergeDuplicate;
		HideZeroPercent = defaults.Achievements.HideZeroPercent;
		NotificationsEnabled = defaults.Notifications.Enabled;
		NotificationSound = defaults.Notifications.PlaySound;
		CheckForUpdates = defaults.CheckForUpdates;
		SteamOfficialEnabled = defaults.SteamOfficialEnabled;
		ShadPs4Enabled = defaults.ShadPs4Enabled;

		WatchedDirectories = new ObservableCollection<DirectoryItemViewModel>(defaults.WatchedDirectories.Select(d => new DirectoryItemViewModel(d)));
		ProtonPrefixDirectories = new ObservableCollection<DirectoryItemViewModel>(defaults.ProtonPrefixDirectories.Select(d => new DirectoryItemViewModel(d)));

		StatusMessage = "Settings reset to defaults.";
	}

	[RelayCommand]
	private async Task CheckForUpdateAsync()
	{
		if (_checkForUpdate is null)
			return;

		await _checkForUpdate();
	}
}

public sealed partial class DirectoryItemViewModel(DirectoryConfig config)
	: ObservableObject
{
	[ObservableProperty]
	public partial string Path { get; set; } = config.Path;

	[ObservableProperty]
	public partial string Label { get; set; } = config.Label;

	[ObservableProperty]
	public partial bool Enabled { get; set; } = config.Enabled;

	public bool IsDefault { get; } = config.IsDefault;

	// Companion property so the view can bind directly instead of using a negated binding.
	public bool IsRemovable => !IsDefault;

	public DirectoryConfig ToConfig() => new()
	{
		Path = Path,
		Label = Label,
		Enabled = Enabled,
		IsDefault = IsDefault
	};
}
