using System.Text.RegularExpressions;
using System.Xml.Linq;
using TinyTrophy.Models;

namespace TinyTrophy.Services.Scanners;

/// <summary>
/// Scans the ShadPS4 emulator trophy directories for PS4 achievements.
/// Schema is read from %APPDATA%\shadPS4\trophy\{NPWR}\Xml\TROP*.XML
/// Icons from %APPDATA%\shadPS4\trophy\{NPWR}\Icons\TROP*.PNG
/// Progress from %APPDATA%\shadPS4\home\{userId}\trophy\{NPWR}.xml
/// </summary>
public sealed partial class ShadPs4Scanner(ISettingsService settings)
	: IAchievementScanner
{
	private static readonly string ShadPs4Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "shadPS4");

	[GeneratedRegex(@"^TROP_\d{2}\.xml$", RegexOptions.IgnoreCase, "en-GB")]
	private static partial Regex ThrophXmlRegex();

	public AchievementSource Source => AchievementSource.ShadPs4;
	public string DisplayName => "ShadPS4 trophies";

	public Task<IReadOnlyList<Game>> ParseAsync(
		IProgress<(int current, int total)>? progress = null,
		CancellationToken ct = default)
	{
		List<Game> games = [];

		if (!settings.Settings.ShadPs4Enabled)
			return Task.FromResult<IReadOnlyList<Game>>(games);

		string trophyRoot = Path.Combine(ShadPs4Root, "trophy");
		if (!Directory.Exists(trophyRoot))
			return Task.FromResult<IReadOnlyList<Game>>(games);

		string[] npwrDirs;
		try
		{
			npwrDirs = Directory.GetDirectories(trophyRoot);
		}
		catch
		{
			return Task.FromResult<IReadOnlyList<Game>>(games);
		}

		// Collect all user progress directories
		Dictionary<string, List<string>> progressFilesByNpwr = GetProgressFilesByNpwr();

		int total = npwrDirs.Length;
		int current = 0;

		foreach (string npwrDir in npwrDirs)
		{
			ct.ThrowIfCancellationRequested();
			current++;
			progress?.Report((current, total));

			try
			{
				string npwrId = Path.GetFileName(npwrDir);
				string xmlDir = Path.Combine(npwrDir, "Xml");
				string iconsDir = Path.Combine(npwrDir, "Icons");

				if (!Directory.Exists(xmlDir))
					continue;

				TrophySet? trophySet = ParseTrophySet(xmlDir, npwrId);
				if (trophySet is null || trophySet.Trophies.Count == 0)
					continue;

				// Apply progress from all user files, taking the latest unlock
				if (progressFilesByNpwr.TryGetValue(npwrId.ToUpperInvariant(), out List<string>? progressFiles))
				{
					foreach (string progressFile in progressFiles)
						ApplyProgress(trophySet, progressFile);
				}

				List<Achievement> achievements = [.. trophySet.Trophies.Select(t => ToAchievement(t, iconsDir))];

				if (achievements.Count == 0)
					continue;

				games.Add(new Game
				{
					AppId = npwrId,
					Name = trophySet.Title ?? $"ShadPS4: {npwrId}",
					ImageUri = GetGameImageUri(iconsDir),
					Source = AchievementSource.ShadPs4,
					Achievements = achievements
				});
			}
			catch { }
		}

		return Task.FromResult<IReadOnlyList<Game>>(games);
	}

	/// <summary>
	/// Parses a single NPWR trophy directory to extract all achievements and their progress.
	/// Used by GameWatcherService for real-time detection.
	/// </summary>
	public static List<Achievement> ParseNpwrDirectory(string npwrId)
	{
		string trophyRoot = Path.Combine(ShadPs4Root, "trophy");
		string npwrDir = Path.Combine(trophyRoot, npwrId);
		string xmlDir = Path.Combine(npwrDir, "Xml");
		string iconsDir = Path.Combine(npwrDir, "Icons");

		if (!Directory.Exists(xmlDir))
			return [];

		TrophySet? trophySet = ParseTrophySet(xmlDir, npwrId);
		if (trophySet is null || trophySet.Trophies.Count == 0)
			return [];

		// Apply progress from all users
		string homeDir = Path.Combine(ShadPs4Root, "home");
		if (Directory.Exists(homeDir))
		{
			foreach (string userDir in Directory.EnumerateDirectories(homeDir))
			{
				string progressFile = Path.Combine(userDir, "trophy", $"{npwrId}.xml");
				if (File.Exists(progressFile))
					ApplyProgress(trophySet, progressFile);
			}
		}

		return [.. trophySet.Trophies.Select(t => ToAchievement(t, iconsDir))];
	}

	/// <summary>
	/// Returns true if the given file path is a ShadPS4 trophy progress file.
	/// </summary>
	public static bool IsShadPs4ProgressFile(string filePath)
	{
		// Progress files are at: %APPDATA%\shadPS4\home\{userId}\trophy\{NPWR}.xml
		if (!filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
			return false;

		string? dir = Path.GetDirectoryName(filePath);
		if (dir is null)
			return false;

		return Path.GetFileName(dir).Equals("trophy", StringComparison.OrdinalIgnoreCase)
			&& filePath.Contains(Path.Combine("shadPS4", "home"), StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Extracts the NPWR ID from a ShadPS4 progress file path.
	/// </summary>
	public static string? GetNpwrIdFromProgressFile(string filePath)
	{
		if (!IsShadPs4ProgressFile(filePath))
			return null;

		return Path.GetFileNameWithoutExtension(filePath);
	}

	/// <summary>
	/// Returns the ShadPS4 home directory path, or null if it doesn't exist.
	/// </summary>
	public static string? GetShadPs4HomeDir()
	{
		string homeDir = Path.Combine(ShadPs4Root, "home");
		return Directory.Exists(homeDir) ? homeDir : null;
	}

	private static Dictionary<string, List<string>> GetProgressFilesByNpwr()
	{
		Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
		string homeDir = Path.Combine(ShadPs4Root, "home");

		if (!Directory.Exists(homeDir))
			return result;

		try
		{
			foreach (string userDir in Directory.EnumerateDirectories(homeDir))
			{
				string userTrophyDir = Path.Combine(userDir, "trophy");
				if (!Directory.Exists(userTrophyDir))
					continue;

				foreach (string xmlFile in Directory.EnumerateFiles(userTrophyDir, "*.xml"))
				{
					string npwrId = Path.GetFileNameWithoutExtension(xmlFile).ToUpperInvariant();
					if (!result.TryGetValue(npwrId, out List<string>? list))
					{
						list = [];
						result[npwrId] = list;
					}
					list.Add(xmlFile);
				}
			}
		}
		catch { }

		return result;
	}

	private static TrophySet? ParseTrophySet(
		string xmlDir,
		string npwrId)
	{
		string[] xmlFiles = GetTropXmlFiles(xmlDir);
		if (xmlFiles.Length == 0)
			return null;

		// Find the base file (prefer English TROP_01.xml, then TROP.xml, then first)
		string baseFile = xmlFiles.FirstOrDefault(f =>
				Path.GetFileName(f).Equals("TROP_01.xml", StringComparison.OrdinalIgnoreCase))
			?? xmlFiles.FirstOrDefault(f =>
				Path.GetFileName(f).Equals("TROP.xml", StringComparison.OrdinalIgnoreCase))
			?? xmlFiles[0];

		XDocument baseDoc;
		try
		{
			baseDoc = XDocument.Load(baseFile);
		}
		catch
		{
			return null;
		}

		string title = baseDoc.Descendants("title-name").FirstOrDefault()?.Value.Trim()
			?? baseDoc.Descendants("npcommid").FirstOrDefault()?.Value.Trim()
			?? npwrId;

		Dictionary<string, TrophyEntry> trophies = [];

		// Use only the base file (TROP_01.xml or TROP.xml) for achievement metadata
		foreach (XElement el in baseDoc.Descendants("trophy"))
		{
			string? id = el.Attribute("id")?.Value;
			if (id is null)
				continue;

			if (!trophies.TryGetValue(id, out TrophyEntry? entry))
			{
				entry = new TrophyEntry { Id = id };
				trophies[id] = entry;
			}

			string hidden = el.Attribute("hidden")?.Value ?? "no";
			if (hidden.Equals("yes", StringComparison.OrdinalIgnoreCase))
				entry.IsHidden = true;

			string? ttype = el.Attribute("ttype")?.Value;
			if (!string.IsNullOrEmpty(ttype))
				entry.TrophyType = ttype.Trim().ToLowerInvariant();

			string? name = el.Element("name")?.Value.Trim();
			if (!string.IsNullOrEmpty(name))
				entry.Name = name;

			string? detail = el.Element("detail")?.Value.Trim();
			if (!string.IsNullOrEmpty(detail))
				entry.Description = detail;
		}

		return new TrophySet
		{
			NpwrId = npwrId,
			Title = title,
			Trophies = [.. trophies.Values.OrderBy(t => int.TryParse(t.Id, out int n) ? n : int.MaxValue)]
		};
	}

	private static void ApplyProgress(
		TrophySet trophySet,
		string progressFile)
	{
		try
		{
			XDocument doc = XDocument.Load(progressFile);
			foreach (XElement el in doc.Descendants("trophy"))
			{
				string? id = el.Attribute("id")?.Value;
				if (id is null)
					continue;

				TrophyEntry? entry = trophySet.Trophies.FirstOrDefault(t => t.Id == id);
				if (entry is null)
					continue;

				string unlockState = el.Attribute("unlockstate")?.Value ?? "";
				string unlocked = el.Attribute("unlocked")?.Value ?? "";

				bool isUnlocked = unlockState.Equals("true", StringComparison.OrdinalIgnoreCase)
					|| unlocked.Equals("yes", StringComparison.OrdinalIgnoreCase);

				if (isUnlocked)
				{
					entry.IsUnlocked = true;

					string? tsRaw = el.Attribute("timestamp")?.Value;
					if (tsRaw is not null && long.TryParse(tsRaw, out long ts) && ts > 0)
					{
						DateTime unlockTime = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
						// Keep the latest unlock time if multiple users
						if (entry.UnlockTime is null || unlockTime > entry.UnlockTime)
							entry.UnlockTime = unlockTime;
					}
				}
			}
		}
		catch { }
	}

	private static Achievement ToAchievement(
		TrophyEntry entry,
		string iconsDir)
	{
		string iconFileName = $"TROP{entry.Id.PadLeft(3, '0')}.PNG";
		string iconPath = Path.Combine(iconsDir, iconFileName);
		string iconUri = File.Exists(iconPath) ? new Uri(iconPath).AbsoluteUri : string.Empty;

		return new Achievement
		{
			Id = entry.Id,
			Name = entry.Name ?? string.Empty,
			Description = entry.Description ?? string.Empty,
			IconUri = iconUri,
			IconLockedUri = iconUri,
			IsUnlocked = entry.IsUnlocked,
			UnlockTime = entry.UnlockTime,
			IsHidden = entry.IsHidden,
			TrophyType = entry.TrophyType
		};
	}

	/// <summary>
	/// Returns the game header image URI from the trophy Icons directory.
	/// Prefers GR001.PNG (trophy group banner), falls back to ICON0.PNG (game icon).
	/// </summary>
	private static string GetGameImageUri(string iconsDir)
	{
		string banner = Path.Combine(iconsDir, "GR001.PNG");
		if (File.Exists(banner))
			return new Uri(banner).AbsoluteUri;

		string icon = Path.Combine(iconsDir, "ICON0.PNG");
		if (File.Exists(icon))
			return new Uri(icon).AbsoluteUri;

		return string.Empty;
	}

	private static string[] GetTropXmlFiles(string xmlDir)
	{
		try
		{
			return [.. Directory.GetFiles(xmlDir, "TROP*.xml", SearchOption.TopDirectoryOnly)
				.Where(f =>
				{
					string name = Path.GetFileName(f);
					return name.Equals("TROP.xml", StringComparison.OrdinalIgnoreCase)
						|| ThrophXmlRegex().IsMatch(name);
				})
				.OrderBy(f => f)];
		}
		catch
		{
			return [];
		}
	}

	private sealed class TrophySet
	{
		public string NpwrId { get; set; } = string.Empty;
		public string? Title { get; set; }
		public List<TrophyEntry> Trophies { get; set; } = [];
	}

	private sealed class TrophyEntry
	{
		public string Id { get; set; } = string.Empty;
		public string? Name { get; set; }
		public string? Description { get; set; }
		public string TrophyType { get; set; } = "unknown";
		public bool IsHidden { get; set; }
		public bool IsUnlocked { get; set; }
		public DateTime? UnlockTime { get; set; }
	}
}
