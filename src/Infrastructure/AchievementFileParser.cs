using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure;

/// <summary>
/// Unified parser for achievement save files.
/// Priority order: achievements.json > user_stats.ini > achievements.ini > stats.bin
/// </summary>
public static class AchievementFileParser
{
	// Recognized achievement file names
	internal static readonly string[] AchievementFileNames =
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

	// Extra fallback display-name keys used only by the object format
	private static readonly string[] NameFallbackKeys = ["name", "Name"];

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
			// Parse forward-only from raw UTF-8 bytes with Utf8JsonReader instead of JsonDocument.
			// JsonDocument builds and retains an in-memory tree for the whole file (and every
			// materialized string is transcoded from UTF-8 back to UTF-16 through JsonDocument.GetString,
			// regardless of the input encoding), so this avoids that document allocation entirely and
			// only materializes the handful of string values actually kept per achievement.
			byte[] utf8Json = File.ReadAllBytes(filePath);
			Utf8JsonReader reader = new(utf8Json);
			if (!reader.Read())
				return [];

			return reader.TokenType switch
			{
				JsonTokenType.StartArray => ParseJsonArray(ref reader),
				JsonTokenType.StartObject => ParseJsonObject(ref reader),
				_ => []
			};
		}
		catch { }

		return [];
	}

	private static List<Achievement> ParseJsonArray(ref Utf8JsonReader reader)
	{
		List<Achievement> achievements = [];
		while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
		{
			if (reader.TokenType != JsonTokenType.StartObject)
			{
				reader.Skip();
				continue;
			}

			(string? id, string? displayName, _, bool earned, DateTime? time) = ReadAchievementFields(ref reader, includeIdKeys: true);
			if (string.IsNullOrEmpty(id))
				continue;

			if (!earned && time.HasValue)
				earned = true;

			achievements.Add(new Achievement
			{
				Id = id,
				Name = displayName ?? string.Empty,
				IsUnlocked = earned,
				UnlockTime = time
			});
		}
		return achievements;
	}

	private static List<Achievement> ParseJsonObject(ref Utf8JsonReader reader)
	{
		List<Achievement> achievements = [];
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			if (reader.TokenType != JsonTokenType.PropertyName)
				continue;

			string sectionName = reader.GetString() ?? string.Empty;
			reader.Read();

			if (reader.TokenType != JsonTokenType.StartObject)
			{
				reader.Skip();
				continue;
			}

			(_, string? displayName, string? nameFallback, bool earned, DateTime? time) = ReadAchievementFields(ref reader, includeIdKeys: false);
			if (!earned && time.HasValue)
				earned = true;

			achievements.Add(new Achievement
			{
				Id = sectionName,
				Name = displayName ?? nameFallback ?? string.Empty,
				IsUnlocked = earned,
				UnlockTime = time
			});
		}
		return achievements;
	}

	// Reads the properties of the achievement object currently being iterated (reader positioned
	// right after its StartObject) in a single forward pass, classifying each property name against
	// the known key lists in priority order — mirroring the previous per-key TryGetProperty lookups
	// but without materializing a JsonDocument tree or allocating strings for properties never used.
	private static (string? Id, string? DisplayName, string? NameFallback, bool Earned, DateTime? Time) ReadAchievementFields(
		ref Utf8JsonReader reader,
		bool includeIdKeys)
	{
		string? id = null;
		int idPriority = int.MaxValue;
		string? displayName = null;
		int namePriority = int.MaxValue;
		string? nameFallback = null;
		int nameFallbackPriority = int.MaxValue;
		bool earned = false;
		int earnedPriority = int.MaxValue;
		DateTime? time = null;
		int timePriority = int.MaxValue;

		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			if (reader.TokenType != JsonTokenType.PropertyName)
				continue;

			int idIdx = includeIdKeys ? MatchKeyIndex(ref reader, IdKeys) : -1;
			int nameIdx = MatchKeyIndex(ref reader, DisplayNameKeys);
			int nameFallbackIdx = MatchKeyIndex(ref reader, NameFallbackKeys);
			int earnedIdx = MatchKeyIndex(ref reader, EarnedKeys);
			int timeIdx = MatchKeyIndex(ref reader, TimeKeys);

			reader.Read();
			JsonTokenType valueKind = reader.TokenType;

			if (idIdx >= 0 && idIdx < idPriority && valueKind == JsonTokenType.String)
			{
				id = reader.GetString();
				idPriority = idIdx;
			}
			else if (nameIdx >= 0 && nameIdx < namePriority && valueKind == JsonTokenType.String)
			{
				displayName = reader.GetString();
				namePriority = nameIdx;
			}
			else if (nameFallbackIdx >= 0 && nameFallbackIdx < nameFallbackPriority && valueKind == JsonTokenType.String)
			{
				nameFallback = reader.GetString();
				nameFallbackPriority = nameFallbackIdx;
			}
			else if (earnedIdx >= 0 && earnedIdx < earnedPriority)
			{
				earned = valueKind switch
				{
					JsonTokenType.True => true,
					JsonTokenType.False => false,
					JsonTokenType.Number => reader.GetInt32() == 1,
					JsonTokenType.String => reader.GetString() is "1" or "true" or "yes",
					_ => false
				};
				earnedPriority = earnedIdx;
			}
			else if (timeIdx >= 0 && timeIdx < timePriority)
			{
				long t = 0;
				if (valueKind == JsonTokenType.Number)
					t = reader.GetInt64();
				else if (valueKind == JsonTokenType.String && long.TryParse(reader.GetString(), out long parsed))
					t = parsed;

				if (t > 0)
				{
					long normalized = t < 10_000_000_000 ? t : t / 1000;
					time = DateTimeOffset.FromUnixTimeSeconds(normalized).LocalDateTime;
					timePriority = timeIdx;
				}
			}
			else if (valueKind is JsonTokenType.StartObject or JsonTokenType.StartArray)
			{
				reader.Skip();
			}
		}

		return (id, displayName, nameFallback, earned, time);
	}

	// Zero-allocation match: compares the current PropertyName token against each candidate key
	// (in priority order) without allocating the property name string.
	private static int MatchKeyIndex(ref Utf8JsonReader reader, string[] keys)
	{
		for (int i = 0; i < keys.Length; i++)
		{
			if (reader.ValueTextEquals(keys[i]))
				return i;
		}
		return -1;
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

				string? displayName = TryGetIniValue(values, DisplayNameKeys) ?? string.Empty;
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

		// Copy out only the first 8 non-space hex digits instead of allocating a
		// cleaned copy of the whole input on every call.
		Span<char> digits = stackalloc char[8];
		int count = 0;
		foreach (char c in hex)
		{
			if (c == ' ')
				continue;
			digits[count++] = c;
			if (count == 8)
				break;
		}

		if (count < 8)
			return false;

		Span<byte> bytes = stackalloc byte[4];
		if (Convert.FromHexString(digits, bytes, out _, out int written) != OperationStatus.Done || written != 4)
			return false;

		result = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
		return true;
	}

	#endregion
}
