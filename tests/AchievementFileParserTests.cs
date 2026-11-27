using System.Buffers.Binary;
using System.Text;
using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class AchievementFileParserTests
{
	private const long UnlockTimeSeconds = 1_700_000_000;

	private static DateTime ExpectedUnlockTime =>
		DateTimeOffset.FromUnixTimeSeconds(UnlockTimeSeconds).LocalDateTime;

	[Fact]
	public void ObjectFormat_ReadsUnlockStateAndTime()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", $$"""
			{
				"ACH_WIN": { "achieved": 1, "UnlockTime": {{UnlockTimeSeconds}} },
				"ACH_LOCKED": { "achieved": 0 }
			}
			""");

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(temp.Root);

		Assert.Equal(2, achievements.Count);

		Achievement won = achievements.Single(a => a.Id == "ACH_WIN");
		Assert.True(won.IsUnlocked);
		Assert.Equal(ExpectedUnlockTime, won.UnlockTime);

		Achievement locked = achievements.Single(a => a.Id == "ACH_LOCKED");
		Assert.False(locked.IsUnlocked);
		Assert.Null(locked.UnlockTime);
	}

	[Fact]
	public void ObjectFormat_UsesFallbackDisplayName()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", """
			{ "ACH_1": { "achieved": 1, "name": "Winner" } }
			""");

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.Equal("ACH_1", achievement.Id);
		Assert.Equal("Winner", achievement.Name);
	}

	[Fact]
	public void ObjectFormat_IgnoresMetadataObjects()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", """
			{
				"meta": { "version": 2, "author": "emu" },
				"ACH_1": { "achieved": 1 }
			}
			""");

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(temp.Root);

		Achievement achievement = Assert.Single(achievements);
		Assert.Equal("ACH_1", achievement.Id);
	}

	[Fact]
	public void ArrayFormat_ReadsEpicFields()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", $$"""
			[
				{ "AchievementId": "ACH_A", "UnlockTime": {{UnlockTimeSeconds}} },
				{ "name": "ACH_B", "achieved": 0 }
			]
			""");

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(temp.Root);

		Achievement unlocked = achievements.Single(a => a.Id == "ACH_A");
		Assert.True(unlocked.IsUnlocked);
		Assert.Equal(ExpectedUnlockTime, unlocked.UnlockTime);

		Achievement locked = achievements.Single(a => a.Id == "ACH_B");
		Assert.False(locked.IsUnlocked);
	}

	[Fact]
	public void RecognizedKeyHoldingAContainer_IsSkippedWithoutLosingLaterFields()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", $$"""
			{
				"ACH_1": {
					"achieved": 1,
					"time": { "nested": "value" },
					"UnlockTime": {{UnlockTimeSeconds}}
				}
			}
			""");

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.True(achievement.IsUnlocked);
		Assert.Equal(ExpectedUnlockTime, achievement.UnlockTime);
	}

	[Fact]
	public void ObjectFormat_WithAnOutOfRangeTimestamp_KeepsTheOtherAchievements()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", $$"""
			{
				"ACH_BAD": { "achieved": 1, "UnlockTime": 999999999999999999 },
				"ACH_GOOD": { "achieved": 1, "UnlockTime": {{UnlockTimeSeconds}} }
			}
			""");

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(temp.Root);

		Assert.Equal(2, achievements.Count);
		Assert.Null(achievements.Single(a => a.Id == "ACH_BAD").UnlockTime);
		Assert.Equal(ExpectedUnlockTime, achievements.Single(a => a.Id == "ACH_GOOD").UnlockTime);
	}

	[Fact]
	public void Ini_WithAnOutOfRangeTimestamp_KeepsTheOtherAchievements()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.ini", $$"""
			[ACH_BAD]
			Achieved=1
			UnlockTime=999999999999999999

			[ACH_GOOD]
			Achieved=1
			UnlockTime={{UnlockTimeSeconds}}
			""");

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(temp.Root);

		Assert.Equal(2, achievements.Count);
		Assert.Null(achievements.Single(a => a.Id == "ACH_BAD").UnlockTime);
		Assert.Equal(ExpectedUnlockTime, achievements.Single(a => a.Id == "ACH_GOOD").UnlockTime);
	}

	[Fact]
	public void Ini_IgnoresMetadataSections()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.ini", $$"""
			[General]
			Language=english

			[ACH_1]
			Achieved=1
			UnlockTime={{UnlockTimeSeconds}}

			[ACH_2]
			Achieved=0
			""");

		List<Achievement> achievements = AchievementFileParser.ParseFromDirectory(temp.Root);

		Assert.Equal(2, achievements.Count);
		Assert.DoesNotContain(achievements, a => a.Id == "General");

		Achievement unlocked = achievements.Single(a => a.Id == "ACH_1");
		Assert.True(unlocked.IsUnlocked);
		Assert.Equal(ExpectedUnlockTime, unlocked.UnlockTime);

		Assert.False(achievements.Single(a => a.Id == "ACH_2").IsUnlocked);
	}

	[Fact]
	public void Ini_ReadsHexState()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.ini", """
			[ACH_HEX]
			State=00000001
			""");

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.True(achievement.IsUnlocked);
	}

	[Fact]
	public void Ini_DecodesUtf16WithoutBom()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.ini", Encoding.Unicode.GetBytes("[ACH_1]\r\nAchieved=1\r\n"));

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.True(achievement.IsUnlocked);
	}

	[Fact]
	public void JsonTakesPriorityOverIni()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", """{ "ACH_JSON": { "achieved": 1 } }""");
		temp.CreateFile("achievements.ini", """
			[ACH_INI]
			Achieved=1
			""");

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.Equal("ACH_JSON", achievement.Id);
	}

	[Fact]
	public void FallsBackToCommonSubdirectories()
	{
		using TempDirectory temp = new();
		temp.CreateFile(Path.Combine("stats", "achievements.json"), """{ "ACH_NESTED": { "achieved": 1 } }""");

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.Equal("ACH_NESTED", achievement.Id);
	}

	[Fact]
	public void StatsBin_ReadsEntries()
	{
		using TempDirectory temp = new();

		byte[] data = new byte[8];
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 0x11223344);
		BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 1);
		temp.CreateFile("stats.bin", data);

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.Equal("11223344", achievement.Id);
		Assert.True(achievement.IsUnlocked);
	}

	[Fact]
	public void MissingDirectory_ReturnsEmpty()
	{
		using TempDirectory temp = new();

		Assert.Empty(AchievementFileParser.ParseFromDirectory(Path.Combine(temp.Root, "does-not-exist")));
	}

	[Theory]
	[InlineData("480", true)]
	[InlineData("1234567890", true)]
	[InlineData("NPWR12345_00", false)]
	[InlineData("", false)]
	public void IsAppId_MatchesDigitsOnly(
		string name,
		bool expected)
	{
		Assert.Equal(expected, AchievementFileParser.IsAppId(name));
	}

	[Theory]
	[InlineData("achievements.json", true)]
	[InlineData("ACHIEVEMENTS.INI", true)]
	[InlineData("user_stats.ini", true)]
	[InlineData("stats.bin", true)]
	[InlineData("trophies.xml", false)]
	public void IsAchievementFile_MatchesKnownNames(
		string fileName,
		bool expected)
	{
		Assert.Equal(expected, AchievementFileParser.IsAchievementFile(fileName));
	}

	[Fact]
	public void EmptyJson_FallsBackToTheIniFile()
	{
		using TempDirectory temp = new();
		temp.CreateFile("achievements.json", "{}");
		temp.CreateFile("achievements.ini", """
			[ACH_INI]
			Achieved=1
			""");

		Achievement achievement = Assert.Single(AchievementFileParser.ParseFromDirectory(temp.Root));

		Assert.Equal("ACH_INI", achievement.Id);
		Assert.True(achievement.IsUnlocked);
	}

	[Fact]
	public void StatsBin_TruncatedFile_ReturnsEmpty()
	{
		using TempDirectory temp = new();
		temp.CreateFile("stats.bin", new byte[] { 1, 2, 3, 4 });

		Assert.Empty(AchievementFileParser.ParseFromDirectory(temp.Root));
	}
}
