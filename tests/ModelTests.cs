using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class ModelTests
{
	[Fact]
	public void Game_CompletionPercentage_WithNoAchievements_IsZero()
	{
		Assert.Equal(0, new Game().CompletionPercentage);
	}

	[Fact]
	public void Game_CompletionPercentage_CountsUnlockedAchievements()
	{
		Game game = new()
		{
			Achievements =
			[
				new Achievement { IsUnlocked = true },
				new Achievement { IsUnlocked = false }
			]
		};

		Assert.Equal(50, game.CompletionPercentage);
		Assert.Equal(1, game.UnlockedCount);
		Assert.Equal(2, game.TotalCount);
	}

	[Fact]
	public void Game_CompletionPercentage_WithEverythingUnlocked_IsHundred()
	{
		Game game = new()
		{
			Achievements = [new Achievement { IsUnlocked = true }, new Achievement { IsUnlocked = true }]
		};

		Assert.Equal(100, game.CompletionPercentage);
	}

	[Fact]
	public void AppSettings_DefaultsMatchTheProductDefaults()
	{
		AppSettings settings = new();

		Assert.Equal("english", settings.Language);
		Assert.True(settings.SteamOfficialEnabled);
		Assert.True(settings.ShadPs4Enabled);
		Assert.True(settings.CloseToTray);
		Assert.True(settings.CheckForUpdates);
		Assert.False(settings.StartMinimized);
		Assert.True(settings.Achievements.MergeDuplicate);
		Assert.True(settings.Achievements.HideZeroPercent);
		Assert.False(settings.Achievements.ShowHidden);
		Assert.True(settings.Notifications.Enabled);
		Assert.True(settings.Notifications.PlaySound);
		Assert.True(settings.HomeSort.Ascending);
		Assert.True(settings.GameDetailSort.Ascending);
	}

	[Theory]
	[InlineData(ApiKeyValidationResult.Valid, "")]
	[InlineData(ApiKeyValidationResult.Invalid, ApiKeyWarningMessages.Invalid)]
	[InlineData(ApiKeyValidationResult.Unreachable, ApiKeyWarningMessages.Unreachable)]
	public void ApiKeyWarningMessages_MapEveryResult(
		ApiKeyValidationResult result,
		string expected)
	{
		Assert.Equal(expected, ApiKeyWarningMessages.FromResult(result));
	}

	[Fact]
	public void ApiKeyWarningMessages_AreDistinct()
	{
		Assert.NotEqual(ApiKeyWarningMessages.Invalid, ApiKeyWarningMessages.Unreachable);
		Assert.NotEmpty(ApiKeyWarningMessages.Invalid);
		Assert.NotEmpty(ApiKeyWarningMessages.Unreachable);
	}
}
