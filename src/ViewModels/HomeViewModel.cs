using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.ViewModels;

public sealed partial class HomeViewModel(
	IAchievementService achievementService,
	MainViewModel mainViewModel,
	ISettingsService settingsService,
	ISteamApiService steamApi)
	: ObservableObject
{
	// One entry per source and game, as scanned; _allGames is rebuilt from it by merging duplicates
	private List<Game> _sourceGames = [];
	private List<Game> _allGames = [];

	// Games that changed while a full load was running, refreshed once it completes
	private readonly HashSet<(AchievementSource Source, string GameId)> _pendingChanges = [];
	private bool _isFullLoadRunning;

	// Set when a reload is asked for while one is already running, so it is not silently dropped
	private bool _reloadRequested;

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
	[NotifyPropertyChangedFor(nameof(HasLoadError))]
	public partial string LoadError { get; set; } = string.Empty;

	public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

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
		// The empty state is computed during the scan, while IsLoading is still true, so it is refreshed
		// here once loading actually finishes
		IsEmpty = Games.Count == 0 && !value && !HasLoadError;

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
	private Task LoadGamesAsync() => LoadGamesCoreAsync();

	/// <summary>
	/// Reloads every game on demand. A reload requested while one is already running is queued and run
	/// once the current one finishes, so it can never be silently dropped.
	/// </summary>
	public async Task ReloadGamesAsync()
	{
		if (_isFullLoadRunning)
		{
			_reloadRequested = true;
			return;
		}

		await LoadGamesCoreAsync();
	}

	private async Task LoadGamesCoreAsync()
	{
		_isFullLoadRunning = true;
		_pendingChanges.Clear();
		LoadError = string.Empty;
		IsLoading = true;
		IsScanning = true;
		ScanningProgress = 0;
		ScanningStatus = "Scanning for achievements...";
		LoadingStatus = "Scanning for achievements...";
		LoadingProgress = 0;
		EnrichedCount = 0;
		TotalGameCount = 0;

		// Start the key check without blocking the local scan: an offline launch otherwise pays the
		// HTTP timeout before any local achievement file is read.
		Task<ApiKeyValidationResult>? apiKeyValidation = null;
		string apiKey = settingsService.Settings.SteamApiKey?.Trim() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(apiKey))
			apiKeyValidation = steamApi.ValidateApiKeyAsync(apiKey);
		else
			mainViewModel.ApiKeyWarning = string.Empty;

		try
		{
			// Scan local achievement files and official Steam achievements
			Progress<(string scannerName, int current, int total)> scanProgress = new(p =>
			{
				ScanningProgress = (double)p.current / p.total * 100;
				ScanningStatus = $"Scanning {p.scannerName}... ({p.current}/{p.total})";
			});
			// The local scanners read every achievement file synchronously, so the scan is kept off the UI
			// thread; the Progress<T> above marshals its reports back to the dispatcher
			IReadOnlyList<Game> games = await Task.Run(() => achievementService.ScanGamesAsync(scanProgress));
			_sourceGames = [.. games];
			_allGames = [.. achievementService.MergeGames(_sourceGames)];
			TotalGameCount = _allGames.Count;

			ApplyFilter();

			// Done scanning. The list stays covered until the load is over: the scanned rows still
			// carry bare AppIDs and no icons until enrichment has run
			IsScanning = false;
			IsLoading = false;
			IsEnriching = TotalGameCount > 0;
			if (IsEnriching)
			{
				LoadingProgress = 0;
				LoadingStatus = $"Fetching metadata... (0/{TotalGameCount})";
			}

			// Enrichment needs Steam, so wait for the key check here rather than before the local scan
			if (apiKeyValidation is not null)
				await apiKeyValidation;

			if (TotalGameCount == 0)
			{
				UserProfile = achievementService.GetUserProfile(_allGames);
			}
			else
			{
				Progress<int> progress = new(p =>
				{
					int done = (int)(p / 100.0 * TotalGameCount);
					EnrichedCount = done;
					LoadingProgress = p;
					LoadingStatus = $"Fetching metadata... ({done}/{TotalGameCount})";
				});

				await achievementService.EnrichGamesAsync(_sourceGames, progress);

				// Refresh the UI with the enriched data
				EnrichedCount = TotalGameCount;
				LoadingProgress = 100;
				RebuildGames();
			}
		}
		catch (Exception ex)
		{
			LoadError = $"Could not load achievements: {ex.Message}";
			IsEmpty = false;
		}
		finally
		{
			IsLoading = false;
			IsEnriching = false;
			_isFullLoadRunning = false;
		}

		await ApplyPendingChangesAsync();

		// A reload asked for while this one was running (e.g. settings saved mid-scan) runs now
		if (_reloadRequested)
		{
			_reloadRequested = false;
			await LoadGamesCoreAsync();
		}
	}

	/// <summary>
	/// Updates a single game after its achievement files changed, without rescanning everything.
	/// Must be called on the UI thread.
	/// </summary>
	public async Task RefreshGameAsync(
		AchievementSource source,
		string gameId)
	{
		// The full load will read the latest files anyway, but it may have already passed this game
		if (_isFullLoadRunning)
		{
			_pendingChanges.Add((source, gameId));
			return;
		}

		IReadOnlyList<Game>? updated;
		try
		{
			// Re-reading a game touches disk, so it is kept off the UI thread
			updated = await Task.Run(() => achievementService.ScanGameAsync(source, gameId));
		}
		catch
		{
			updated = null;
		}

		// The source can't refresh single games, so a full reload is queued instead
		if (updated is null)
		{
			_reloadRequested = true;
			if (!_isFullLoadRunning)
			{
				_reloadRequested = false;
				await LoadGamesCoreAsync();
			}

			return;
		}

		// A full load may have started while scanning, and will pick up this change itself
		if (_isFullLoadRunning)
			return;

		_sourceGames.RemoveAll(g => g.Source == source && g.AppId == gameId);
		_sourceGames.AddRange(updated);
		RebuildGames();
	}

	private async Task ApplyPendingChangesAsync()
	{
		if (_pendingChanges.Count == 0)
			return;

		List<(AchievementSource Source, string GameId)> changes = [.. _pendingChanges];
		_pendingChanges.Clear();

		foreach ((AchievementSource source, string gameId) in changes)
			await RefreshGameAsync(source, gameId);
	}

	/// <summary>
	/// Merges the per-source games, updates the list and stats, and refreshes the open detail page.
	/// </summary>
	private void RebuildGames()
	{
		_allGames = [.. achievementService.MergeGames(_sourceGames)];
		TotalGameCount = _allGames.Count;
		UserProfile = achievementService.GetUserProfile(_allGames);
		ApplyFilter();

		// If a game detail page is open, update it with the new data
		if (mainViewModel.ActiveView is GameDetailViewModel detailVm)
		{
			Game? updatedGame = _allGames.FirstOrDefault(g => g.AppId == detailVm.GameAppId);
			if (updatedGame is null || (updatedGame.UnlockedCount == 0 && settingsService.Settings.Achievements.HideZeroPercent))
				mainViewModel.GoBackCommand.Execute(null);
			else
				detailVm.RefreshFromGame(updatedGame);
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

		IsEmpty = Games.Count == 0 && !IsLoading && !HasLoadError;
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
	public string CompletionText { get; } = $"{game.UnlockedCount}/{game.TotalCount}";
	public string PercentText { get; } = $"{game.CompletionPercentage:F0}%";
}

public enum SortMode
{
	Alphabetical,
	Completion,
	Recent
}
