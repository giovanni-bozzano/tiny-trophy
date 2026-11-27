using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class AchievementMergeTests
{
	private static AchievementService CreateService(bool mergeDuplicates = true) =>
		new(
			[],
			[],
			new FakeSettingsService
			{
				Settings = new AppSettings
				{
					Achievements = new AchievementsSettings { MergeDuplicate = mergeDuplicates }
				}
			});

	private static Achievement Achievement(
		string id,
		bool unlocked = false,
		DateTime? unlockTime = null) => new()
	{
		Id = id,
		Name = id,
		IsUnlocked = unlocked,
		UnlockTime = unlockTime
	};

	private static Game Game(string appId, string name, params Achievement[] achievements) => new()
	{
		AppId = appId,
		Name = name,
		Source = AchievementSource.SteamEmulator,
		Achievements = [.. achievements]
	};

	[Fact]
	public void MergeGames_WhenDisabled_ReturnsTheInputUntouched()
	{
		Game first = Game("480", "First", Achievement("A"));
		Game second = Game("480", "Second", Achievement("B"));

		IReadOnlyList<Game> merged = CreateService(mergeDuplicates: false).MergeGames([first, second]);

		Assert.Equal(2, merged.Count);
		Assert.Same(first, merged[0]);
		Assert.Same(second, merged[1]);
	}

	[Fact]
	public void MergeGames_WithASingleEntry_ReturnsTheSameInstance()
	{
		Game game = Game("480", "Only", Achievement("A"));

		Game merged = Assert.Single(CreateService().MergeGames([game]));

		Assert.Same(game, merged);
	}

	[Fact]
	public void MergeGames_CombinesTheAchievementsOfTheSameGame()
	{
		Game primary = Game("480", "Primary", Achievement("A"), Achievement("B"));
		Game other = Game("480", "Other", Achievement("C"));

		Game merged = Assert.Single(CreateService().MergeGames([primary, other]));

		Assert.Equal(3, merged.TotalCount);
		Assert.Equal(new[] { "A", "B", "C" }, merged.Achievements.Select(a => a.Id));
	}

	[Fact]
	public void MergeGames_KeepsASingleEntryPerAppId()
	{
		Game merged = Assert.Single(CreateService().MergeGames(
			[
				Game("480", "One", Achievement("A")),
				Game("480", "Two", Achievement("B")),
				Game("480", "Three", Achievement("C"))
			]));

		Assert.Equal("One", merged.Name);
		Assert.Equal(3, merged.TotalCount);
	}

	[Fact]
	public void MergeGames_DoesNotMergeDifferentGames()
	{
		IReadOnlyList<Game> merged = CreateService().MergeGames(
			[
				Game("480", "Half-Life", Achievement("A")),
				Game("730", "Counter-Strike", Achievement("B"))
			]);

		Assert.Equal(2, merged.Count);
		Assert.Equal(new[] { "480", "730" }, merged.Select(g => g.AppId));
	}

	[Fact]
	public void MergeGames_PromotesALockedAchievementWithItsUnlockTime()
	{
		DateTime unlockedAt = new(2024, 5, 1, 12, 0, 0, DateTimeKind.Local);
		Game primary = Game("480", "Primary", Achievement("A"));
		Game other = Game("480", "Other", Achievement("A", unlocked: true, unlockTime: unlockedAt));

		Game merged = Assert.Single(CreateService().MergeGames([primary, other]));

		Achievement achievement = Assert.Single(merged.Achievements);
		Assert.True(achievement.IsUnlocked);
		Assert.Equal(unlockedAt, achievement.UnlockTime);
	}

	[Fact]
	public void MergeGames_KeepsTheExistingUnlockTimeOfAnUnlockedAchievement()
	{
		DateTime earlier = new(2023, 1, 1, 8, 0, 0, DateTimeKind.Local);
		DateTime later = new(2024, 1, 1, 8, 0, 0, DateTimeKind.Local);
		Game primary = Game("480", "Primary", Achievement("A", unlocked: true, unlockTime: earlier));
		Game other = Game("480", "Other", Achievement("A", unlocked: true, unlockTime: later));

		Game merged = Assert.Single(CreateService().MergeGames([primary, other]));

		Assert.Equal(earlier, Assert.Single(merged.Achievements).UnlockTime);
	}

	[Fact]
	public void MergeGames_MatchesIdsCaseInsensitively()
	{
		Game primary = Game("480", "Primary", Achievement("ACH_ONE"));
		Game other = Game("480", "Other", Achievement("ach_one", unlocked: true));

		Game merged = Assert.Single(CreateService().MergeGames([primary, other]));

		Achievement achievement = Assert.Single(merged.Achievements);
		Assert.Equal("ACH_ONE", achievement.Id);
		Assert.True(achievement.IsUnlocked);
	}

	[Fact]
	public void MergeGames_LeavesTheInputsUnchanged()
	{
		Game primary = Game("480", "Primary", Achievement("A"));
		Game other = Game("480", "Other", Achievement("B", unlocked: true));

		Game merged = Assert.Single(CreateService().MergeGames([primary, other]));

		Assert.NotSame(primary, merged);
		Assert.Single(primary.Achievements);
		Assert.False(primary.Achievements[0].IsUnlocked);
		Assert.Single(other.Achievements);
	}

	[Fact]
	public void MergeGames_CopiesUnlockStateSoALaterMergeIsStillCorrect()
	{
		AchievementService service = CreateService();
		Game primary = Game("480", "Primary", Achievement("A"));
		Game other = Game("480", "Other", Achievement("A", unlocked: true));

		service.MergeGames([primary, other]);
		Game mergedAgain = Assert.Single(service.MergeGames([primary, other]));

		Assert.True(Assert.Single(mergedAgain.Achievements).IsUnlocked);
		Assert.False(Assert.Single(primary.Achievements).IsUnlocked);
	}
}
