using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TinyTrophy.Models;
using TinyTrophy.Services;

namespace TinyTrophy.ViewModels;

public sealed partial class HomeViewModel(
	IAchievementService achievementService,
	MainViewModel mainViewModel,
	ISettingsService settingsService,
	ISteamApiService steamApi)
	: ObservableObject
{
	private List<Game> _allGames = [];

	[ObservableProperty]
	public partial ObservableCollection<GameItemViewModel> Games { get; set; } = [];

	[ObservableProperty]
	public partial string SearchText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsLoading { get; set; }

	[ObservableProperty]
	public partial bool IsScanning { get; set; }

	[ObservableProperty]
	public partial double ScanningProgress { get; set; }

	[ObservableProperty]
	public partial string ScanningStatus { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsEnriching { get; set; }

	[ObservableProperty]
	public partial double LoadingProgress { get; set; }

	[ObservableProperty]
	public partial string LoadingStatus { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int EnrichedCount { get; set; }

	[ObservableProperty]
	public partial int TotalGameCount { get; set; }

	[ObservableProperty]
	public partial UserProfile UserProfile { get; set; } = new();

	[ObservableProperty]
	public partial SortMode CurrentSort { get; set; } =
		Enum.TryParse(settingsService.Settings.HomeSort.Mode, out SortMode s) ? s : SortMode.Completion;

	[ObservableProperty]
	public partial bool SortAscending { get; set; } = settingsService.Settings.HomeSort.Ascending;

	[ObservableProperty]
	public partial bool IsEmpty { get; set; }

	public bool IsListVisible => !IsLoading && !IsEnriching;

	public string StatsAchievements => IsLoading || IsEnriching ? "N/A" : UserProfile.TotalAchievements.ToString();
	public string StatsPerfect => IsLoading || IsEnriching ? "N/A" : UserProfile.PerfectGames.ToString();
	public string StatsGames => IsLoading || IsEnriching ? "N/A" : UserProfile.TotalGames.ToString();
	public string StatsCompletion => IsLoading || IsEnriching ? "N/A" : $"{UserProfile.OverallCompletion:F1}%";

	public string AlphabeticalSortLabel => CurrentSort == SortMode.Alphabetical ? (SortAscending ? "A-Z \u2193" : "A-Z \u2191") : "A-Z";
	public string CompletionSortLabel => CurrentSort == SortMode.Completion ? (SortAscending ? "% \u2193" : "% \u2191") : "%";
	public string TimeSortLabel => CurrentSort == SortMode.Recent ? (SortAscending ? "Time \u2193" : "Time \u2191") : "Time";

	partial void OnSearchTextChanged(string value)
	{
		ApplyFilter();
	}

	partial void OnIsLoadingChanged(bool value)
	{
		OnPropertyChanged(nameof(IsListVisible));
		NotifyStatsChanged();
	}

	partial void OnIsEnrichingChanged(bool value)
	{
		OnPropertyChanged(nameof(IsListVisible));
		NotifyStatsChanged();
	}

	partial void OnUserProfileChanged(UserProfile value) => NotifyStatsChanged();

	private void NotifyStatsChanged()
	{
		OnPropertyChanged(nameof(StatsAchievements));
		OnPropertyChanged(nameof(StatsPerfect));
		OnPropertyChanged(nameof(StatsGames));
		OnPropertyChanged(nameof(StatsCompletion));
	}

	[RelayCommand]
	private async Task LoadGamesAsync()
	{
		IsLoading = true;
		IsScanning = true;
		ScanningProgress = 0;
		ScanningStatus = "Scanning for achievements...";
		LoadingStatus = "Scanning for achievements...";
		LoadingProgress = 0;
		EnrichedCount = 0;
		TotalGameCount = 0;

		// Validate API key before loading
		string apiKey = settingsService.Settings.SteamApiKey;
		if (!string.IsNullOrWhiteSpace(apiKey))
			mainViewModel.ApiKeyWarning = ApiKeyWarningMessages.FromResult(await steamApi.ValidateApiKeyAsync(apiKey));
		else
			mainViewModel.ApiKeyWarning = string.Empty;
		mainViewModel.PrivateProfileWarning = string.Empty;
		steamApi.ResetPrivacyFlag();

		try
		{
			// Scan local achievement files and official Steam achievements
			Progress<(string scannerName, int current, int total)> scanProgress = new(p =>
			{
				ScanningProgress = (double)p.current / p.total * 100;
				ScanningStatus = $"Scanning {p.scannerName}... ({p.current}/{p.total})";
			});
			IReadOnlyList<Game> games = await achievementService.ScanGamesAsync(scanProgress);
			_allGames = [.. games];
			TotalGameCount = _allGames.Count;

			if (steamApi.PrivacyErrorDetected)
				mainViewModel.PrivateProfileWarning = "Your Steam game details are private. Set them to Public in Steam \u2192 Settings \u2192 Privacy to load all achievements.";

			ApplyFilter();

			// Done scanning, show the game list
			IsScanning = false;
			IsLoading = false;

			if (TotalGameCount == 0)
				return;

			// Fetch metadata in the background (games remain visible)
			IsEnriching = true;
			LoadingProgress = 0;
			LoadingStatus = $"Fetching metadata... (0/{TotalGameCount})";
			Progress<int> progress = new(p =>
			{
				int done = (int)(p / 100.0 * TotalGameCount);
				EnrichedCount = done;
				LoadingProgress = p;
				LoadingStatus = $"Fetching metadata... ({done}/{TotalGameCount})";
			});

			await achievementService.EnrichGamesAsync(games, progress);

			// Refresh the UI with the enriched data
			EnrichedCount = TotalGameCount;
			UserProfile = achievementService.GetUserProfile(games);
			ApplyFilter();
			LoadingProgress = 100;

			// If a game detail page is open, update it with the new metadata
			if (mainViewModel.CurrentView is GameDetailViewModel detailVm)
			{
				Game? updatedGame = _allGames.FirstOrDefault(g => g.AppId == detailVm.GameAppId);
				if (updatedGame is null || (updatedGame.UnlockedCount == 0 && settingsService.Settings.Achievements.HideZeroPercent))
					mainViewModel.GoBackCommand.Execute(null);
				else
					detailVm.RefreshFromGame(updatedGame);
			}
		}
		catch (Exception ex)
		{
			LoadingStatus = $"Error: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
			IsEnriching = false;
		}
	}

	private void SetSort(SortMode mode)
	{
		if (CurrentSort == mode)
		{
			SortAscending = !SortAscending;
		}
		else
		{
			CurrentSort = mode;
			SortAscending = true;
		}
		OnPropertyChanged(nameof(AlphabeticalSortLabel));
		OnPropertyChanged(nameof(CompletionSortLabel));
		OnPropertyChanged(nameof(TimeSortLabel));

		settingsService.Settings.HomeSort.Mode = CurrentSort.ToString();
		settingsService.Settings.HomeSort.Ascending = SortAscending;
		_ = settingsService.SaveAsync();

		ApplyFilter();
	}

	[RelayCommand]
	private void SortAlphabetical() => SetSort(SortMode.Alphabetical);

	[RelayCommand]
	private void SortByCompletion() => SetSort(SortMode.Completion);

	[RelayCommand]
	private void SortByRecent() => SetSort(SortMode.Recent);

	[RelayCommand]
	private void OpenGame(GameItemViewModel? gameVm)
	{
		if (gameVm?.Game is null)
			return;

		GameDetailViewModel detailVm = new(gameVm.Game, settingsService.Settings.Achievements.ShowHidden, settingsService);
		mainViewModel.NavigateToGameDetail(detailVm);
	}

	private void ApplyFilter()
	{
		IEnumerable<Game> filtered = _allGames.AsEnumerable();

		if (settingsService.Settings.Achievements.HideZeroPercent)
			filtered = filtered.Where(g => g.UnlockedCount > 0);

		if (!string.IsNullOrWhiteSpace(SearchText))
		{
			filtered = filtered.Where(g =>
				g.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
		}

		IOrderedEnumerable<Game> sorted = (CurrentSort, SortAscending) switch
		{
			(SortMode.Completion, true) => filtered.OrderByDescending(g => g.CompletionPercentage).ThenBy(g => g.Name),
			(SortMode.Completion, false) => filtered.OrderBy(g => g.CompletionPercentage).ThenBy(g => g.Name),
			(SortMode.Recent, true) => filtered.OrderByDescending(g => g.Achievements.Where(a => a.IsUnlocked && a.UnlockTime.HasValue).Max(a => a.UnlockTime) ?? DateTime.MinValue),
			(SortMode.Recent, false) => filtered.OrderBy(g => g.Achievements.Where(a => a.IsUnlocked && a.UnlockTime.HasValue).Max(a => a.UnlockTime) ?? DateTime.MinValue),
			(_, true) => filtered.OrderBy(g => g.Name),
			(_, false) => filtered.OrderByDescending(g => g.Name)
		};

		Games = new ObservableCollection<GameItemViewModel>(sorted.Select(g => new GameItemViewModel(g)));

		IsEmpty = Games.Count == 0 && !IsLoading;
	}
}

public sealed partial class GameItemViewModel(Game game)
	: ObservableObject
{
	public Game Game { get; } = game;
	public string Name => Game.Name;
	public string AppId => Game.AppId;
	public string ImageUri => Game.ImageUri;
	public int UnlockedCount => Game.UnlockedCount;
	public int TotalCount => Game.TotalCount;
	public double CompletionPercentage => Game.CompletionPercentage;
	public string CompletionText => $"{UnlockedCount}/{TotalCount}";
	public string PercentText => $"{CompletionPercentage:F0}%";
}

public enum SortMode
{
	Alphabetical,
	Completion,
	Recent
}
