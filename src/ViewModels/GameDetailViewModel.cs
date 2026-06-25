using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using TinyTrophy.Models;
using TinyTrophy.Services;

namespace TinyTrophy.ViewModels;

public sealed partial class GameDetailViewModel
	: ObservableObject
{
	private Game _game;
	private readonly bool _showHidden;
	private readonly ISettingsService _settingsService;
	private List<AchievementItemViewModel> _allAchievements = [];

	[ObservableProperty]
	public partial string GameName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string GameImageUri { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int UnlockedCount { get; set; }

	[ObservableProperty]
	public partial int TotalCount { get; set; }

	[ObservableProperty]
	public partial double CompletionPercentage { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<AchievementItemViewModel> UnlockedAchievements { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<AchievementItemViewModel> LockedAchievements { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<AchievementItemViewModel> DisplayAchievements { get; set; } = [];

	[ObservableProperty]
	public partial string SearchText { get; set; } = string.Empty;

	[ObservableProperty]
	public partial AchievementSortMode CurrentSort { get; set; } = AchievementSortMode.UnlockTime;

	[ObservableProperty]
	public partial bool SortAscending { get; set; } = true;

	[ObservableProperty]
	public partial bool ShowUnlocked { get; set; } = true;

	[ObservableProperty]
	public partial bool ShowLocked { get; set; } = true;

	public string GameAppId => _game.AppId;

	public string AlphabeticalSortLabel => CurrentSort == AchievementSortMode.Alphabetical
		? (SortAscending ? "A-Z \u2193" : "A-Z \u2191")
		: "A-Z";
	public string RaritySortLabel => CurrentSort == AchievementSortMode.Rarity
		? (SortAscending ? "Rarity \u2193" : "Rarity \u2191")
		: "Rarity";
	public string TimeSortLabel => CurrentSort == AchievementSortMode.UnlockTime
		? (SortAscending ? "Time \u2193" : "Time \u2191")
		: "Time";

	public GameDetailViewModel(
		Game game,
		bool showHidden,
		ISettingsService settingsService)
	{
		_game = game;
		_showHidden = showHidden;
		_settingsService = settingsService;

		if (Enum.TryParse<AchievementSortMode>(settingsService.Settings.GameDetailSort.Mode, out var savedSort))
			CurrentSort = savedSort;
		SortAscending = settingsService.Settings.GameDetailSort.Ascending;

		GameName = game.Name;
		GameImageUri = game.ImageUri;
		UnlockedCount = game.UnlockedCount;
		TotalCount = game.TotalCount;
		CompletionPercentage = game.CompletionPercentage;

		_allAchievements = [.. game.Achievements.Select(a => new AchievementItemViewModel(a, _showHidden))];

		ApplyFilter();
	}

	partial void OnSearchTextChanged(string value) => ApplyFilter();

	/// <summary>
	/// Rebuilds the view after metadata enrichment finishes, updating rarity, icons, and names.
	/// </summary>
	public void RefreshFromGame()
	{
		GameName = _game.Name;
		GameImageUri = _game.ImageUri;
		UnlockedCount = _game.UnlockedCount;
		TotalCount = _game.TotalCount;
		CompletionPercentage = _game.CompletionPercentage;

		_allAchievements = [.. _game.Achievements.Select(a => new AchievementItemViewModel(a, _showHidden))];

		ApplyFilter();
	}

	public void RefreshFromGame(Game updatedGame)
	{
		_game = updatedGame;
		RefreshFromGame();
	}

	private void SetSort(AchievementSortMode mode)
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
		OnPropertyChanged(nameof(RaritySortLabel));
		OnPropertyChanged(nameof(TimeSortLabel));

		_settingsService.Settings.GameDetailSort.Mode = CurrentSort.ToString();
		_settingsService.Settings.GameDetailSort.Ascending = SortAscending;
		_ = _settingsService.SaveAsync();

		ApplyFilter();
	}

	[RelayCommand]
	private void SortAlphabetical() => SetSort(AchievementSortMode.Alphabetical);

	[RelayCommand]
	private void SortByRarity() => SetSort(AchievementSortMode.Rarity);

	[RelayCommand]
	private void SortByTime() => SetSort(AchievementSortMode.UnlockTime);

	[RelayCommand]
	private void ToggleUnlocked()
	{
		ShowUnlocked = !ShowUnlocked;
		ApplyFilter();
	}

	[RelayCommand]
	private void ToggleLocked()
	{
		ShowLocked = !ShowLocked;
		ApplyFilter();
	}

	private void ApplyFilter()
	{
		IEnumerable<AchievementItemViewModel> items = _allAchievements.AsEnumerable();

		if (!string.IsNullOrWhiteSpace(SearchText))
		{
			items = items.Where(a =>
				a.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
				a.DisplayDescription.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
		}

		IOrderedEnumerable<AchievementItemViewModel> sorted = (CurrentSort, SortAscending) switch
		{
			(AchievementSortMode.Rarity, true) => items.OrderBy(a => a.GlobalPercentage),
			(AchievementSortMode.Rarity, false) => items.OrderByDescending(a => a.GlobalPercentage),
			(AchievementSortMode.UnlockTime, true) => items.OrderByDescending(a => a.UnlockTime ?? DateTime.MinValue).ThenBy(a => a.Name),
			(AchievementSortMode.UnlockTime, false) => items.OrderBy(a => a.UnlockTime ?? DateTime.MinValue).ThenBy(a => a.Name),
			(_, true) => items.OrderBy(a => a.Name),
			(_, false) => items.OrderByDescending(a => a.Name)
		};

		List<AchievementItemViewModel> list = [.. sorted];
		List<AchievementItemViewModel> unlocked = [.. list.Where(a => a.IsUnlocked)];
		List<AchievementItemViewModel> locked = [.. list.Where(a => !a.IsUnlocked).OrderBy(a => a.IsMasked)];

		UnlockedAchievements = new ObservableCollection<AchievementItemViewModel>(unlocked);
		LockedAchievements = new ObservableCollection<AchievementItemViewModel>(locked);

		// Flat list for the virtualized ListBox: unlocked first, then locked
		DisplayAchievements = new ObservableCollection<AchievementItemViewModel>(unlocked.Concat(locked));
	}
}

public sealed class AchievementItemViewModel(
	Achievement achievement,
	bool showHidden)
{
	// True when this achievement is hidden and the user hasn't enabled "show hidden".
	// Unlocked achievements always show their real info regardless.
	public bool IsMasked => achievement.IsHidden && !showHidden && !achievement.IsUnlocked;

	public string Id => achievement.Id;
	public string Name => !string.IsNullOrEmpty(achievement.Name) ? achievement.Name : HumanizeId(achievement.Id);
	public string Description => achievement.Description;
	public string IconUri => achievement.IsUnlocked ? achievement.IconUri : achievement.IconLockedUri;

	// Masked variants shown when the achievement is hidden and locked
	public string DisplayName => IsMasked ? "Hidden Achievement" : Name;
	public string DisplayDescription => IsMasked ? "Unlock this achievement to reveal its details." : Description;
	public string DisplayIconUri => IsMasked ? string.Empty : IconUri;

	public bool IsUnlocked => achievement.IsUnlocked;
	public DateTime? UnlockTime => achievement.UnlockTime;
	public string UnlockTimeText => achievement.UnlockTime?.ToString("g") ?? "";

	// Visual styling for unlocked vs locked cards
	public string CardBackground => IsUnlocked ? "#1c2b3c" : "#15202d";
	public double CardOpacity => IsUnlocked ? 1.0 : 0.55;
	public string IconBackground => IsUnlocked ? "#27374a" : "#1c2b3c";
	public string NameForeground => IsUnlocked ? "#d9dfe4" : "#8f98a0";
	public string DescriptionForeground => IsUnlocked ? "#8f98a0" : "#6b7c8d";

	public double GlobalPercentage => achievement.GlobalPercentage;
	public string RarityText => achievement.GlobalPercentage > 0 ? $"{achievement.GlobalPercentage:F1}%" : "";
	public string RarityLabel => achievement.GlobalPercentage > 0
		? achievement.GlobalPercentage switch
		{
			<= 5.0 => $"Ultra Rare · {achievement.GlobalPercentage:F1}% of players",
			<= 10.0 => $"Rare · {achievement.GlobalPercentage:F1}% of players",
			<= 25.0 => $"Uncommon · {achievement.GlobalPercentage:F1}% of players",
			_ => $"Common · {achievement.GlobalPercentage:F1}% of players"
		}
		: achievement.TrophyType switch
		{
			"p" => "Platinum",
			"g" => "Gold",
			"s" => "Silver",
			"b" => "Bronze",
			_ => ""
		};
	public string RarityColor => achievement.GlobalPercentage > 0
		? achievement.GlobalPercentage switch
		{
			<= 5.0 => "#b9f2ff",
			<= 10.0 => "#66c0f4",
			<= 25.0 => "#98d982",
			_ => "#8f98a0"
		}
		: achievement.TrophyType switch
		{
			"p" => "#a0b2c8",
			"g" => "#ffd700",
			"s" => "#c0c0c0",
			"b" => "#cd7f32",
			_ => "#6b7c8d"
		};
	public bool IsRare => achievement.GlobalPercentage is > 0 and <= 10
		|| achievement.TrophyType is "Platinum" or "Gold";
	public bool IsHidden => achievement.IsHidden;

	/// <summary>
	/// Converts IDs like "ACH_COMPLETE_GAME" or "completeGame" into "Complete Game".
	/// </summary>
	private static string HumanizeId(string id)
	{
		if (string.IsNullOrEmpty(id))
			return id;

		// Remove common prefixes
		string stripped = id;
		foreach (string? prefix in new[] { "ACH_", "ACHIEVE_", "ACHIEVEMENT_", "ach_", "achieve_", "achievement_" })
		{
			if (stripped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				stripped = stripped[prefix.Length..];
				break;
			}
		}

		// Replace underscores and hyphens with spaces
		StringBuilder sb = new(stripped.Length + 8);
		for (int i = 0; i < stripped.Length; i++)
		{
			char c = stripped[i];
			if (c == '_' || c == '-')
			{
				sb.Append(' ');
			}
			else if (i > 0 && char.IsUpper(c) && char.IsLower(stripped[i - 1]))
			{
				// camelCase split
				sb.Append(' ');
				sb.Append(c);
			}
			else
			{
				sb.Append(c);
			}
		}

		// Title-case the result
		string result = sb.ToString().Trim();
		if (result.Length == 0)
			return id;

		string[] words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < words.Length; i++)
		{
			if (words[i].Length > 0)
				words[i] = char.ToUpper(words[i][0]) + words[i][1..].ToLower();
		}
		return string.Join(' ', words);
	}
}

public enum AchievementSortMode
{
	Alphabetical,
	Rarity,
	UnlockTime
}
