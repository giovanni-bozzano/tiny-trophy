using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Services;

/// <summary>
/// Unified parser for achievement save files.
/// Priority order: achievements.json > user_stats.ini > achievements.ini > stats.bin
/// </summary>
public static class AchievementFileParser
{
	// Recognized achievement file names
	private static readonly string[] AchievementFileNames =
		["achievements.json", "achievements.ini", "user_stats.ini", "stats.bin"];

	// Subdirectories to scan as fallback
	private static readonly string[] FallbackSubDirs =
		["stats", "Stats", "SteamData", "SteamEmu", "SteamEmu/UserStats"];

	// Possible key names for the achievement ID
	private static readonly string[] IdKeys =
		["id", "ID", "apiname", "apiName", "AchievementId", "achievementId", "name", "Name"];

	// Possible key names for the display name
	private static readonly string[] DisplayNameKeys =
		["displayName", "DisplayName", "display_name", "title", "Title", "label", "Label"];

	// Possible key names for the unlocked state
	private static readonly string[] EarnedKeys =
		["achieved", "Achieved", "ACHIEVED", "earned", "Earned", "Unlock", "unlock", "Unlocked", "unlocked", "UNLOCKED"];

	// Possible key names for the unlock timestamp
	private static readonly string[] TimeKeys =
		["UnlockTime", "unlockTime", "unlock_time", "timestamp", "earned_time", "earnedTime", "UnlockedTime", "unlockedTime", "UNLOCKEDTIME", "Time", "time"];

	/// <summary>
	/// Returns true if the file name is a known achievement data file.
	/// </summary>
	public static bool IsAchievementFile(string fileName)
	{
		foreach (string name in AchievementFileNames)
		{
			if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Returns true if the name looks like a Steam AppID (digits only).
	/// </summary>
	public static bool IsAppId(string name)
	{
		if (string.IsNullOrEmpty(name))
			return false;

		foreach (char c in name)
		{
			if (!char.IsDigit(c))
				return false;
		}

		return true;
	}

	/// <summary>
	/// Parse achievements from a save directory using format auto-detection.
	/// Automatically checks common subdirectories (stats/, SteamData/, etc.) if the
	/// root directory has no results.
	/// </summary>
	public static List<Achievement> ParseFromDirectory(string saveDir)
	{
		if (!Directory.Exists(saveDir))
			return [];

		List<Achievement> result = ParseDirectoryCore(saveDir);
		if (result.Count > 0)
			return result;

		// Check common subdirectories used by various emulators
		foreach (string sub in FallbackSubDirs)
		{
			string subDir = Path.Combine(saveDir, sub);
			if (!Directory.Exists(subDir))
				continue;

			result = ParseDirectoryCore(subDir);
			if (result.Count > 0)
				return result;
		}

		return [];
	}

	private static List<Achievement> ParseDirectoryCore(string dir)
	{
		// 1. achievements.json
		string? jsonPath = FindFile(dir, "achievements.json");
		if (jsonPath is not null)
		{
			List<Achievement> result = ParseAchievementsJson(jsonPath);
			if (result.Count > 0)
				return result;
		}

		// 2. user_stats.ini (Tenoke format)
		string? userStatsPath = FindFile(dir, "user_stats.ini");
		if (userStatsPath is not null)
		{
			List<Achievement> result = ParseAchievementsIni(userStatsPath);
			if (result.Count > 0)
				return result;
		}

		// 3. achievements.ini
		string? iniPath = FindFile(dir, "achievements.ini");
		if (iniPath is not null)
		{
			List<Achievement> result = ParseAchievementsIni(iniPath);
			if (result.Count > 0)
				return result;
		}

		// 4. stats.bin (binary CRC-32 format)
		string? binPath = Path.Combine(dir, "stats.bin");
		if (File.Exists(binPath))
		{
			List<Achievement> result = ParseStatsBin(binPath);
			if (result.Count > 0)
				return result;
		}

		return [];
	}

	/// <summary>
	/// Parse achievements.json supporting multiple formats:
	/// - Object format: { "ACH_NAME": { "achieved": 1, "UnlockTime": 123 } }
	/// - Array format: [{ "name": "ACH_NAME", "achieved": 1 }]
	/// - GOG format: { "ACH_NAME": { "unlock_time": 123 } }
	/// - Epic format: [{ "AchievementId": "ACH_NAME", "UnlockTime": 123 }]
	/// </summary>
	private static List<Achievement> ParseAchievementsJson(string filePath)
	{
		try
		{
			string? json = File.ReadAllText(filePath);
			using JsonDocument doc = JsonDocument.Parse(json);
			JsonElement root = doc.RootElement;

			if (root.ValueKind == JsonValueKind.Array)
				return ParseJsonArray(root);
			if (root.ValueKind == JsonValueKind.Object)
				return ParseJsonObject(root);
		}
		catch { }

		return [];
	}

	private static List<Achievement> ParseJsonArray(JsonElement array)
	{
		List<Achievement> achievements = [];
		foreach (JsonElement item in array.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
				continue;

			string? id = GetJsonString(item, IdKeys);
			if (string.IsNullOrEmpty(id))
				continue;

			string? displayName = GetJsonString(item, DisplayNameKeys) ?? "";
			bool earned = GetJsonBool(item, EarnedKeys);
			DateTime? time = GetJsonTime(item, TimeKeys);

			if (!earned && time.HasValue)
				earned = true;

			achievements.Add(new Achievement
			{
				Id = id,
				Name = displayName,
				IsUnlocked = earned,
				UnlockTime = time
			});
		}
		return achievements;
	}

	private static List<Achievement> ParseJsonObject(JsonElement obj)
	{
		List<Achievement> achievements = [];
		foreach (JsonProperty prop in obj.EnumerateObject())
		{
			if (prop.Value.ValueKind != JsonValueKind.Object)
				continue;

			JsonElement item = prop.Value;
			bool earned = GetJsonBool(item, EarnedKeys);
			DateTime? time = GetJsonTime(item, TimeKeys);

			if (!earned && time.HasValue)
				earned = true;

			string? displayName = GetJsonString(item, DisplayNameKeys) ?? GetJsonString(item, ["name", "Name"]) ?? "";

			achievements.Add(new Achievement
			{
				Id = prop.Name,
				Name = displayName,
				IsUnlocked = earned,
				UnlockTime = time
			});
		}
		return achievements;
	}

	/// <summary>
	/// Parse achievements.ini or user_stats.ini in INI format.
	/// Supports State/Time/CurProgress and Achieved/UnlockTime sections.
	/// </summary>
	private static List<Achievement> ParseAchievementsIni(string filePath)
	{
		try
		{
			string? content = ReadIniWithEncoding(filePath);
			if (content is null)
				return [];

			Dictionary<string, Dictionary<string, string>> sections = ParseIniSections(content);
			List<Achievement> achievements = [];

			foreach ((string? sectionName, Dictionary<string, string>? values) in sections)
			{
				if (sectionName.Equals("SteamAchievements", StringComparison.OrdinalIgnoreCase) ||
					sectionName.Equals("Stats", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string? displayName = TryGetIniValue(values, DisplayNameKeys) ?? "";
				bool earned = IsIniEarned(values);
				DateTime? unlockTime = ParseIniUnlockTime(values, ref earned);

				// State-based (RLD! hex format)
				if (!earned && values.TryGetValue("State", out string? stateVal))
				{
					if (TryParseHexLE32(stateVal, out uint stateNum) && stateNum > 0)
						earned = true;
				}

				// Progress-based: CurProgress >= MaxProgress
				if (!earned)
				{
					int? cur = TryGetIniNumber(values, "CurProgress", "curProgress", "progress", "Progress");
					int? max = TryGetIniNumber(values, "MaxProgress", "maxProgress", "max_progress", "Max", "max");
					if (cur.HasValue && max.HasValue && max.Value > 0 && cur.Value >= max.Value)
						earned = true;
				}

				achievements.Add(new Achievement
				{
					Id = sectionName,
					Name = displayName,
					IsUnlocked = earned,
					UnlockTime = unlockTime
				});
			}

			return achievements;
		}
		catch { }

		return [];
	}

	private static bool IsIniEarned(Dictionary<string, string> values)
	{
		foreach (string key in EarnedKeys)
		{
			if (values.TryGetValue(key, out string? val))
				return val is "1" or "true" or "True" or "yes";
		}
		return false;
	}

	private static DateTime? ParseIniUnlockTime(
		Dictionary<string, string> values,
		ref bool earned)
	{
		foreach (string key in TimeKeys)
		{
			if (values.TryGetValue(key, out string? val) && long.TryParse(val, out long t) && t > 0)
			{
				if (!earned)
					earned = true;
				long normalized = t < 10_000_000_000 ? t : t / 1000;
				return DateTimeOffset.FromUnixTimeSeconds(normalized).LocalDateTime;
			}
		}
		return null;
	}

	private static string? TryGetIniValue(
		Dictionary<string, string> values,
		ReadOnlySpan<string> keys
	)
	{
		foreach (string key in keys)
		{
			if (values.TryGetValue(key, out string? val))
				return val;
		}
		return null;
	}

	/// <summary>
	/// Parse stats.bin (binary format used by some emulators).
	/// CRC-32 of achievement name is used as key, stored in little-endian 4-byte blocks.
	/// </summary>
	private static List<Achievement> ParseStatsBin(string filePath)
	{
		try
		{
			byte[] data = File.ReadAllBytes(filePath);
			if (data.Length < 8)
				return [];

			List<Achievement> achievements = [];
			// stats.bin format: sequences of 4-byte CRC32 + 4-byte state
			// Each entry is at minimum 8 bytes
			int offset = 0;
			while (offset + 8 <= data.Length)
			{
				uint crc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
				uint state = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
				offset += 8;

				bool earned = state > 0;
				long time = 0;

				// Some formats have a timestamp after the state
				if (offset + 4 <= data.Length)
				{
					uint possibleTime = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
					// Heuristic: if it looks like a Unix timestamp (year 2000+)
					if (possibleTime > 946684800 && possibleTime < 2000000000)
					{
						time = possibleTime;
						offset += 4;
					}
				}

				string? crcHex = crc.ToString("x8");
				DateTime? unlockDateTime = time > 0
					? DateTimeOffset.FromUnixTimeSeconds(time).LocalDateTime
					: null;

				achievements.Add(new Achievement
				{
					Id = crcHex,
					IsUnlocked = earned,
					UnlockTime = unlockDateTime
				});
			}

			return achievements;
		}
		catch { }

		return [];
	}

	#region Helpers

	private static string? FindFile(
		string dir,
		string filename)
	{
		if (!Directory.Exists(dir))
			return null;

		try
		{
			foreach (string file in Directory.EnumerateFiles(dir))
			{
				if (Path.GetFileName(file).Equals(filename, StringComparison.OrdinalIgnoreCase))
					return file;
			}
		}
		catch { }
		return null;
	}

	private static string? ReadIniWithEncoding(string filePath)
	{
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			if (bytes.Length < 2)
				return null;

			// Detect BOM
			if (bytes[0] == 0xFF && bytes[1] == 0xFE)
				return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
			if (bytes[0] == 0xFE && bytes[1] == 0xFF)
				return Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\uFEFF');

			// Check for null bytes (indicates UTF-16LE without BOM, e.g. UniverseLAN)
			if (bytes.Contains((byte)0x00))
				return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');

			return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
		}
		catch
		{
			return null;
		}
	}

	private static Dictionary<string, Dictionary<string, string>> ParseIniSections(string content)
	{
		Dictionary<string, Dictionary<string, string>> sections = new(StringComparer.OrdinalIgnoreCase);
		string? currentSection = null;

		foreach (string rawLine in content.Split('\n'))
		{
			string? line = rawLine.Trim().TrimEnd('\r');
			if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
				continue;

			if (line.StartsWith('[') && line.EndsWith(']'))
			{
				currentSection = line[1..^1];
				sections.TryAdd(currentSection, new Dictionary<string, string>(StringComparer.Ordinal));
				continue;
			}

			if (currentSection is null)
				continue;

			int eqIdx = line.IndexOf('=');
			if (eqIdx > 0)
			{
				string? key = line[..eqIdx].Trim();
				string? value = line[(eqIdx + 1)..].Trim();
				sections[currentSection][key] = value;
			}
		}

		return sections;
	}

	private static string? GetJsonString(
		JsonElement elem,
		ReadOnlySpan<string> keys)
	{
		foreach (string key in keys)
		{
			if (elem.TryGetProperty(key, out JsonElement val) && val.ValueKind == JsonValueKind.String)
				return val.GetString();
		}
		return null;
	}

	private static bool GetJsonBool(
		JsonElement elem,
		ReadOnlySpan<string> keys)
	{
		foreach (string key in keys)
		{
			if (!elem.TryGetProperty(key, out JsonElement val))
				continue;

			return val.ValueKind switch
			{
				JsonValueKind.True => true,
				JsonValueKind.Number => val.GetInt32() == 1,
				JsonValueKind.String => val.GetString() is "1" or "true" or "yes",
				_ => false
			};
		}
		return false;
	}

	private static DateTime? GetJsonTime(
		JsonElement elem,
		ReadOnlySpan<string> keys)
	{
		foreach (string key in keys)
		{
			if (!elem.TryGetProperty(key, out JsonElement val))
				continue;

			long t = 0;
			if (val.ValueKind == JsonValueKind.Number)
				t = val.GetInt64();
			else if (val.ValueKind == JsonValueKind.String && long.TryParse(val.GetString(), out long parsed))
				t = parsed;

			if (t > 0)
			{
				long normalized = t < 10_000_000_000 ? t : t / 1000;
				return DateTimeOffset.FromUnixTimeSeconds(normalized).LocalDateTime;
			}
		}
		return null;
	}

	private static int? TryGetIniNumber(
		Dictionary<string, string> values,
		params string[] keys)
	{
		foreach (string key in keys)
		{
			if (values.TryGetValue(key, out string? val) && int.TryParse(val, out int num))
				return num;
		}
		return null;
	}

	private static bool TryParseHexLE32(
		string hex,
		out uint result)
	{
		result = 0;
		string? clean = hex.Replace(" ", "");
		if (clean.Length < 8)
			return false;

		try
		{
			byte[] bytes = Convert.FromHexString(clean[..8]);
			result = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
			return true;
		}
		catch
		{
			return false;
		}
	}

	#endregion
}
