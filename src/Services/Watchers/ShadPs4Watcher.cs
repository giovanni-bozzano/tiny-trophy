using TinyTrophy.Models;
using TinyTrophy.Services.Scanners;

namespace TinyTrophy.Services.Watchers;

/// <summary>
/// Watches ShadPS4 trophy progress files for newly unlocked achievements.
/// Progress files live at: %APPDATA%\shadPS4\home\{userId}\trophy\{NPWR}.xml
/// </summary>
public sealed class ShadPs4Watcher : GameWatcherBase
{
	protected override void InitializeKnownState()
	{
		string? homeDir = ShadPs4Scanner.GetShadPs4HomeDir();
		if (homeDir is null)
			return;

		try
		{
			foreach (string userDir in Directory.EnumerateDirectories(homeDir))
			{
				string userTrophyDir = Path.Combine(userDir, "trophy");
				if (!Directory.Exists(userTrophyDir))
					continue;

				foreach (string xmlFile in Directory.EnumerateFiles(userTrophyDir, "*.xml"))
				{
					string npwrId = Path.GetFileNameWithoutExtension(xmlFile);
					List<Achievement> achievements = ShadPs4Scanner.ParseNpwrDirectory(npwrId);

					HashSet<string> unlocked = achievements
						.Where(a => a.IsUnlocked)
						.Select(a => a.Id)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);

					SetKnownUnlocks($"shadps4:{npwrId}", unlocked);
				}
			}
		}
		catch { }
	}

	protected override void SetupFileWatchers()
	{
		string? homeDir = ShadPs4Scanner.GetShadPs4HomeDir();
		if (homeDir is null)
			return;

		try
		{
			FileSystemWatcher watcher = new(homeDir)
			{
				IncludeSubdirectories = true,
				Filter = "*.xml",
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
				EnableRaisingEvents = true
			};

			watcher.Changed += OnFileChanged;
			watcher.Created += OnFileChanged;
			AddWatcher(watcher);
		}
		catch { }
	}

	private void OnFileChanged(
		object sender,
		FileSystemEventArgs e)
	{
		if (!ShadPs4Scanner.IsShadPs4ProgressFile(e.FullPath))
			return;

		string? npwrId = ShadPs4Scanner.GetNpwrIdFromProgressFile(e.FullPath);
		if (npwrId is not null)
			ScheduleDetection($"shadps4:{npwrId}");
	}

	protected override Task DetectNewAchievementsAsync(string key)
	{
		try
		{
			// Key format is "shadps4:{npwrId}"
			string npwrId = key["shadps4:".Length..];

			List<Achievement> currentUnlocked = [.. ShadPs4Scanner.ParseNpwrDirectory(npwrId).Where(a => a.IsUnlocked)];
			List<Achievement> newAchievements = DiffAndUpdate(key, currentUnlocked);

			foreach (Achievement ach in newAchievements)
			{
				RaiseAchievementUnlocked(npwrId, ach);
			}
		}
		catch { }

		return Task.CompletedTask;
	}
}
