using TinyTrophy.Models;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Tests;

public sealed class GameDetailViewModelTests
{
	private static Game CreateGame() => new()
	{
		AppId = "1",
		Name = "Test Game",
		Achievements =
		[
			new Achievement
			{
				Id = "U",
				Name = "Unlocked",
				IsUnlocked = true,
				UnlockTime = new DateTime(2024, 1, 1)
			},
			new Achievement { Id = "L", Name = "Locked" }
		]
	};

	[Fact]
	public void Constructor_SplitsUnlockedAndLockedAchievements()
	{
		GameDetailViewModel viewModel = new(CreateGame(), showHidden: false, new FakeSettingsService());

		Assert.Single(viewModel.UnlockedAchievements);
		Assert.Single(viewModel.LockedAchievements);
		Assert.Equal(2, viewModel.DisplayAchievements.Count);
		Assert.Equal(50, viewModel.CompletionPercentage, 3);
	}

	[Fact]
	public void ToggleUnlocked_HidesUnlockedAchievements()
	{
		GameDetailViewModel viewModel = new(CreateGame(), showHidden: false, new FakeSettingsService());

		viewModel.ToggleUnlockedCommand.Execute(null);

		Assert.False(viewModel.ShowUnlocked);
		Assert.Empty(viewModel.UnlockedAchievements);
		Assert.Single(viewModel.DisplayAchievements);
		Assert.Equal("L", viewModel.DisplayAchievements[0].Id);

		viewModel.ToggleUnlockedCommand.Execute(null);

		Assert.True(viewModel.ShowUnlocked);
		Assert.Single(viewModel.UnlockedAchievements);
		Assert.Equal(2, viewModel.DisplayAchievements.Count);
	}

	[Fact]
	public void ToggleLocked_HidesLockedAchievements()
	{
		GameDetailViewModel viewModel = new(CreateGame(), showHidden: false, new FakeSettingsService());

		viewModel.ToggleLockedCommand.Execute(null);

		Assert.False(viewModel.ShowLocked);
		Assert.Empty(viewModel.LockedAchievements);
		Assert.Single(viewModel.DisplayAchievements);
		Assert.Equal("U", viewModel.DisplayAchievements[0].Id);
	}

	[Fact]
	public void HiddenLockedAchievement_IsMasked()
	{
		Achievement hidden = new()
		{
			Id = "H",
			Name = "Secret",
			Description = "Spoiler",
			IsHidden = true
		};

		AchievementItemViewModel masked = new(hidden, showHidden: false);
		AchievementItemViewModel revealed = new(hidden, showHidden: true);

		Assert.True(masked.IsMasked);
		Assert.Equal("Hidden Achievement", masked.DisplayName);
		Assert.False(revealed.IsMasked);
		Assert.Equal("Secret", revealed.DisplayName);
	}

	[Fact]
	public void HiddenUnlockedAchievement_IsNeverMasked()
	{
		AchievementItemViewModel item = new(
			new Achievement { Id = "H", Name = "Secret", IsHidden = true, IsUnlocked = true },
			showHidden: false);

		Assert.False(item.IsMasked);
		Assert.Equal("Secret", item.DisplayName);
	}

	[Theory]
	[InlineData("p", true)]
	[InlineData("g", true)]
	[InlineData("s", false)]
	[InlineData("b", false)]
	[InlineData("", false)]
	public void IsRare_MatchesTheLowercaseTrophyTypesShadPs4Stores(
		string trophyType,
		bool expected)
	{
		AchievementItemViewModel item = new(
			new Achievement { Id = "H", TrophyType = trophyType },
			showHidden: true);

		Assert.Equal(expected, item.IsRare);
	}

	[Theory]
	[InlineData("p", "Platinum")]
	[InlineData("g", "Gold")]
	[InlineData("s", "Silver")]
	[InlineData("b", "Bronze")]
	[InlineData("", "")]
	public void RarityLabel_UsesTheTrophyType(
		string trophyType,
		string expected)
	{
		AchievementItemViewModel item = new(
			new Achievement { Id = "H", TrophyType = trophyType },
			showHidden: true);

		Assert.Equal(expected, item.RarityLabel);
	}
}
