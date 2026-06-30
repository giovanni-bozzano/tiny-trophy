using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TinyTrophy.Models;
using TinyTrophy.Services;
using TinyTrophy.Services.Scanners;

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
	public partial ObservableCollection<FolderItemViewModel> WatchedFolders { get; set; } = [];

	[ObservableProperty]
	public partial string NewFolderPath { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string NewFolderLabel { get; set; } = string.Empty;

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

		WatchedFolders = new ObservableCollection<FolderItemViewModel>(s.WatchedFolders.Select(f => new FolderItemViewModel(f)));
	}

	[RelayCommand]
	private void AddFolder()
	{
		string path = NewFolderPath.Trim();
		if (string.IsNullOrWhiteSpace(path))
			return;

		string label = string.IsNullOrWhiteSpace(NewFolderLabel)
			? Path.GetFileName(path)
			: NewFolderLabel.Trim();

		WatchedFolders.Add(new FolderItemViewModel(new WatchedFolderConfig
		{
			Path = path,
			Label = label,
			Enabled = true,
			IsDefault = false
		}));

		NewFolderPath = string.Empty;
		NewFolderLabel = string.Empty;
	}

	[RelayCommand]
	private void RemoveFolder(FolderItemViewModel? folder)
	{
		if (folder is null || folder.IsDefault)
			return;

		WatchedFolders.Remove(folder);
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

		s.WatchedFolders = [.. WatchedFolders.Select(f => f.ToConfig())];

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
		AppSettings defaults = new() { WatchedFolders = SteamEmulatorScanner.GetDefaultFolders() };

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

		WatchedFolders = new ObservableCollection<FolderItemViewModel>(defaults.WatchedFolders.Select(f => new FolderItemViewModel(f)));

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

public sealed partial class FolderItemViewModel(WatchedFolderConfig config)
	: ObservableObject
{
	[ObservableProperty]
	public partial string Path { get; set; } = config.Path;

	[ObservableProperty]
	public partial string Label { get; set; } = config.Label;

	[ObservableProperty]
	public partial bool Enabled { get; set; } = config.Enabled;

	public bool IsDefault { get; } = config.IsDefault;

	public WatchedFolderConfig ToConfig() => new()
	{
		Path = Path,
		Label = Label,
		Enabled = Enabled,
		IsDefault = IsDefault
	};
}
