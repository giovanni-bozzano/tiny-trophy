using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class AchievementServiceTests
{
	private static AchievementService CreateService(
		bool mergeDuplicate = true,
		bool hideZeroPercent = false) =>
		new(
			[],
			[],
			new FakeSettingsService
			{
				Settings = new AppSettings
				{
					Achievements = new AchievementsSettings
					{
						MergeDuplicate = mergeDuplicate,
						HideZeroPercent = hideZeroPercent
					}
				}
			});

	private static Achievement Unlocked(string id, DateTime? time = null) =>
		new() { Id = id, Name = id, IsUnlocked = true, UnlockTime = time };

	private static Achievement Locked(string id) =>
		new() { Id = id, Name = id };

	[Fact]
	public void MergeGames_CombinesEntriesOfTheSameAppId()
	{
		DateTime emulatorUnlock = new(2024, 1, 1);
		DateTime officialUnlock = new(2024, 2, 2);

		Game emulator = new()
		{
			AppId = "480",
			Name = "Emulator",
			Source = AchievementSource.SteamEmulator,
			Achievements = [Unlocked("A", emulatorUnlock), Locked("B")]
		};
		Game official = new()
		{
			AppId = "480",
			Name = "Official",
			Source = AchievementSource.SteamOfficial,
			Achievements = [Unlocked("B", officialUnlock), Locked("C")]
		};

		Game merged = Assert.Single(CreateService().MergeGames([emulator, official]));

		Assert.Equal(3, merged.Achievements.Count);
		Assert.True(merged.Achievements.Single(a => a.Id == "B").IsUnlocked);
		Assert.Equal(officialUnlock, merged.Achievements.Single(a => a.Id == "B").UnlockTime);

		// The inputs are left untouched, so they can be merged again later
		Assert.Equal(2, emulator.Achievements.Count);
		Assert.False(emulator.Achievements.Single(a => a.Id == "B").IsUnlocked);
		Assert.Equal(2, official.Achievements.Count);
	}

	[Fact]
	public void MergeGames_KeepsDifferentAppIdsSeparate()
	{
		Game first = new() { AppId = "1", Name = "First" };
		Game second = new() { AppId = "2", Name = "Second" };

		IReadOnlyList<Game> merged = CreateService().MergeGames([first, second]);

		Assert.Equal(2, merged.Count);
	}

	[Fact]
	public void MergeGames_WhenDisabled_ReturnsTheInputUntouched()
	{
		IReadOnlyList<Game> games = [new Game { AppId = "480" }, new Game { AppId = "480" }];

		Assert.Same(games, CreateService(mergeDuplicate: false).MergeGames(games));
	}

	[Fact]
	public void GetUserProfile_CountsAchievementsPerfectGamesAndCompletion()
	{
		Game perfect = new()
		{
			AppId = "1",
			Achievements = [Unlocked("A"), Unlocked("B")]
		};
		Game partial = new()
		{
			AppId = "2",
			Achievements = [Unlocked("C"), Locked("D"), Locked("E"), Locked("F")]
		};

		UserProfile profile = CreateService().GetUserProfile([perfect, partial]);

		Assert.Equal(2, profile.TotalGames);
		Assert.Equal(3, profile.TotalAchievements);
		Assert.Equal(1, profile.PerfectGames);
		Assert.Equal(62.5, profile.OverallCompletion);
	}

	[Fact]
	public void GetUserProfile_WithHideZeroPercent_SkipsGamesWithoutUnlocks()
	{
		Game zero = new() { AppId = "1", Achievements = [Locked("A"), Locked("B")] };

		UserProfile profile = CreateService(hideZeroPercent: true).GetUserProfile([zero]);

		Assert.Equal(0, profile.TotalGames);
		Assert.Equal(0, profile.PerfectGames);
		Assert.Equal(0, profile.OverallCompletion);
	}

	[Fact]
	public void GetUserProfile_WithNoGames_ReturnsZeros()
	{
		UserProfile profile = CreateService().GetUserProfile([]);

		Assert.Equal(0, profile.TotalGames);
		Assert.Equal(0, profile.TotalAchievements);
		Assert.Equal(0, profile.PerfectGames);
		Assert.Equal(0, profile.OverallCompletion);
	}

	[Fact]
	public void GetUserProfile_GameWithNoAchievements_IsNotPerfect()
	{
		UserProfile profile = CreateService().GetUserProfile([new Game { AppId = "1" }]);

		Assert.Equal(1, profile.TotalGames);
		Assert.Equal(0, profile.TotalAchievements);
		Assert.Equal(0, profile.PerfectGames);
		Assert.Equal(0, profile.OverallCompletion);
	}
}
