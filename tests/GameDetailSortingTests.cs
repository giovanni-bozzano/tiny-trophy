using TinyTrophy.Models;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Tests;

public sealed class GameDetailSortingTests
{
	private static Achievement Achievement(
		string id,
		string? name = null,
		string description = "",
		bool unlocked = false,
		DateTime? unlockTime = null,
		double percentage = 0,
		string icon = "",
		string lockedIcon = "") => new()
	{
		Id = id,
		Name = name ?? id,
		Description = description,
		IsUnlocked = unlocked,
		UnlockTime = unlockTime,
		GlobalPercentage = percentage,
		IconUri = icon,
		IconLockedUri = lockedIcon
	};

	private static Game Game(params Achievement[] achievements) => new()
	{
		AppId = "1",
		Name = "Test Game",
		Achievements = [.. achievements]
	};

	private static GameDetailViewModel Create(
		Game game,
		FakeSettingsService? settings = null) =>
		new(game, showHidden: false, settings ?? new FakeSettingsService());

	[Fact]
	public void Constructor_RestoresTheSavedSort()
	{
		FakeSettingsService settings = new();
		settings.Settings.GameDetailSort.Mode = "Rarity";
		settings.Settings.GameDetailSort.Ascending = false;
		Game game = Game(
			Achievement("common", percentage: 50),
			Achievement("rare", percentage: 5),
			Achievement("uncommon", percentage: 20));

		GameDetailViewModel viewModel = Create(game, settings);

		Assert.Equal(AchievementSortMode.Rarity, viewModel.CurrentSort);
		Assert.False(viewModel.SortAscending);
		Assert.Equal(new[] { "common", "uncommon", "rare" }, viewModel.DisplayAchievements.Select(a => a.Id));
	}

	[Fact]
	public void SortAlphabetical_OrdersByNameAndTogglesTheDirection()
	{
		FakeSettingsService settings = new();
		GameDetailViewModel viewModel = Create(
			Game(Achievement("b", "Bravo"), Achievement("a", "Alpha"), Achievement("c", "Charlie")),
			settings);

		viewModel.SortAlphabeticalCommand.Execute(null);

		Assert.Equal(new[] { "a", "b", "c" }, viewModel.DisplayAchievements.Select(a => a.Id));
		Assert.Equal("A-Z \u2193", viewModel.AlphabeticalSortLabel);
		Assert.Equal("Alphabetical", settings.Settings.GameDetailSort.Mode);
		Assert.True(settings.Settings.GameDetailSort.Ascending);

		viewModel.SortAlphabeticalCommand.Execute(null);

		Assert.Equal(new[] { "c", "b", "a" }, viewModel.DisplayAchievements.Select(a => a.Id));
		Assert.Equal("A-Z \u2191", viewModel.AlphabeticalSortLabel);
		Assert.False(settings.Settings.GameDetailSort.Ascending);
	}

	[Fact]
	public void SortByRarity_OrdersByTheGlobalPercentage()
	{
		GameDetailViewModel viewModel = Create(
			Game(
				Achievement("common", percentage: 50),
				Achievement("rare", percentage: 5),
				Achievement("uncommon", percentage: 20)));

		viewModel.SortByRarityCommand.Execute(null);

		Assert.Equal(new[] { "rare", "uncommon", "common" }, viewModel.DisplayAchievements.Select(a => a.Id));
		Assert.Equal("Rarity \u2193", viewModel.RaritySortLabel);

		viewModel.SortByRarityCommand.Execute(null);

		Assert.Equal(new[] { "common", "uncommon", "rare" }, viewModel.DisplayAchievements.Select(a => a.Id));
		Assert.Equal("Rarity \u2191", viewModel.RaritySortLabel);
	}

	[Fact]
	public void SortByTime_PutsTheMostRecentUnlockFirst()
	{
		FakeSettingsService settings = new();
		settings.Settings.GameDetailSort.Mode = "Alphabetical";
		GameDetailViewModel viewModel = Create(
			Game(
				Achievement("older", unlocked: true, unlockTime: new DateTime(2023, 1, 1)),
				Achievement("newer", unlocked: true, unlockTime: new DateTime(2024, 1, 1)),
				Achievement("locked")),
			settings);

		viewModel.SortByTimeCommand.Execute(null);

		Assert.Equal(
			new[] { "newer", "older", "locked" },
			viewModel.DisplayAchievements.Select(a => a.Id));
		Assert.Equal("Time \u2193", viewModel.TimeSortLabel);
		Assert.Equal("UnlockTime", settings.Settings.GameDetailSort.Mode);
	}

	[Fact]
	public void DisplayAchievements_KeepsUnlockedAchievementsAboveLockedOnes()
	{
		GameDetailViewModel viewModel = Create(
			Game(
				Achievement("zulu", "Zulu", unlocked: true),
				Achievement("alpha", "Alpha")));

		viewModel.SortAlphabeticalCommand.Execute(null);

		Assert.Equal(new[] { "zulu", "alpha" }, viewModel.DisplayAchievements.Select(a => a.Id));
	}

	[Fact]
	public void SearchText_FiltersByNameAndDescription()
	{
		GameDetailViewModel viewModel = Create(
			Game(
				Achievement("a", "Alpha", "The first one"),
				Achievement("b", "Bravo", "A second description"),
				Achievement("c", "Charlie", "The third")));

		viewModel.SearchText = "second";
		Assert.Equal(new[] { "b" }, viewModel.DisplayAchievements.Select(a => a.Id));

		viewModel.SearchText = "ALPHA";
		Assert.Equal(new[] { "a" }, viewModel.DisplayAchievements.Select(a => a.Id));

		viewModel.SearchText = "zzz";
		Assert.Empty(viewModel.DisplayAchievements);

		viewModel.SearchText = "  ";
		Assert.Equal(3, viewModel.DisplayAchievements.Count);
	}

	[Fact]
	public void RefreshFromGame_ReplacesTheGameAndItsCounts()
	{
		GameDetailViewModel viewModel = Create(Game(Achievement("a", "Alpha")));

		viewModel.RefreshFromGame(Game(
			Achievement("b", "Bravo", unlocked: true),
			Achievement("c", "Charlie", unlocked: true)));

		Assert.Equal("1", viewModel.GameAppId);
		Assert.Equal(2, viewModel.UnlockedCount);
		Assert.Equal(2, viewModel.TotalCount);
		Assert.Equal(100, viewModel.CompletionPercentage, 3);
		Assert.Equal(new[] { "b", "c" }, viewModel.DisplayAchievements.Select(a => a.Id));
	}

	[Fact]
	public void RefreshFromGame_WithoutAnArgument_ReloadsTheCurrentGame()
	{
		Game game = Game(Achievement("a", "Alpha"));
		GameDetailViewModel viewModel = Create(game);

		game.Name = "Renamed";
		game.Achievements.Add(Achievement("b", "Bravo", unlocked: true));
		viewModel.RefreshFromGame();

		Assert.Equal("Renamed", viewModel.GameName);
		Assert.Equal(2, viewModel.TotalCount);
		Assert.Equal(1, viewModel.UnlockedCount);
	}

	[Theory]
	[InlineData(4.9, "Ultra Rare \u00B7 4.9% of players")]
	[InlineData(5.0, "Ultra Rare \u00B7 5.0% of players")]
	[InlineData(5.1, "Rare \u00B7 5.1% of players")]
	[InlineData(10.0, "Rare \u00B7 10.0% of players")]
	[InlineData(10.1, "Uncommon \u00B7 10.1% of players")]
	[InlineData(25.0, "Uncommon \u00B7 25.0% of players")]
	[InlineData(25.1, "Common \u00B7 25.1% of players")]
	public void RarityLabel_UsesTheDocumentedThresholds(
		double percentage,
		string expected)
	{
		AchievementItemViewModel item = new(
			Achievement("a", percentage: percentage),
			showHidden: true);

		Assert.Equal(expected, item.RarityLabel);
		Assert.Equal(percentage.ToString("F1") + "%", item.RarityText);
	}

	[Theory]
	[InlineData(4.9, "#b9f2ff")]
	[InlineData(5.0, "#b9f2ff")]
	[InlineData(5.1, "#66c0f4")]
	[InlineData(10.0, "#66c0f4")]
	[InlineData(10.1, "#98d982")]
	[InlineData(25.0, "#98d982")]
	[InlineData(25.1, "#8f98a0")]
	public void RarityColor_UsesTheDocumentedThresholds(
		double percentage,
		string expected)
	{
		AchievementItemViewModel item = new(
			Achievement("a", percentage: percentage),
			showHidden: true);

		Assert.Equal(expected, item.RarityColor);
	}

	[Fact]
	public void Item_UsesTheLockedIconUntilTheAchievementIsUnlocked()
	{
		AchievementItemViewModel locked = new(
			Achievement("a", icon: "unlocked.png", lockedIcon: "locked.png"),
			showHidden: true);
		AchievementItemViewModel unlocked = new(
			Achievement("a", unlocked: true, icon: "unlocked.png", lockedIcon: "locked.png"),
			showHidden: true);

		Assert.Equal("locked.png", locked.DisplayIconUri);
		Assert.Equal("unlocked.png", unlocked.DisplayIconUri);
	}

	[Fact]
	public void MaskedItem_HidesItsDetails()
	{
		AchievementItemViewModel item = new(
			new Achievement
			{
				Id = "a",
				Name = "Secret",
				Description = "Spoiler",
				IsHidden = true,
				IconUri = "icon.png",
				IconLockedUri = "locked.png"
			},
			showHidden: false);

		Assert.True(item.IsMasked);
		Assert.Equal("Hidden Achievement", item.DisplayName);
		Assert.Equal("Unlock this achievement to reveal its details.", item.DisplayDescription);
		Assert.Equal(string.Empty, item.DisplayIconUri);
	}

	[Fact]
	public void UnlockTimeText_IsFormattedOnlyForUnlockedAchievements()
	{
		DateTime unlockTime = new(2024, 3, 5, 7, 8, 9);

		AchievementItemViewModel unlocked = new(
			Achievement("a", unlocked: true, unlockTime: unlockTime),
			showHidden: true);
		AchievementItemViewModel locked = new(Achievement("b"), showHidden: true);

		Assert.Equal(unlockTime.ToString("g"), unlocked.UnlockTimeText);
		Assert.Equal(string.Empty, locked.UnlockTimeText);
	}

	[Fact]
	public void Item_ExposesTheUnderlyingAchievementData()
	{
		Achievement achievement = new()
		{
			Id = "ach_one",
			Name = string.Empty,
			Description = "Desc",
			IsUnlocked = true
		};

		AchievementItemViewModel item = new(achievement, showHidden: true);

		Assert.Equal("ach_one", item.Id);
		Assert.Equal("ach_one", item.Name);
		Assert.Equal("ach_one", item.DisplayName);
		Assert.Equal("Desc", item.Description);
		Assert.True(item.IsUnlocked);
		Assert.Equal("#1c2b3c", item.CardBackground);
		Assert.Equal(1.0, item.CardOpacity);
	}
}
