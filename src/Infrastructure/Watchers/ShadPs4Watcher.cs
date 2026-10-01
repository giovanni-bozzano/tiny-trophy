using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure.Watchers;

/// <summary>
/// Watches ShadPS4 trophy progress files for newly unlocked achievements.
/// Progress files live at: %APPDATA%\shadPS4\home\{userId}\trophy\{NPWR}.xml
/// </summary>
public sealed class ShadPs4Watcher(ISettingsService settings) : GameWatcherBase
{
	protected override AchievementSource Source => AchievementSource.ShadPs4;

	protected override void InitializeKnownState()
	{
		if (!settings.Settings.ShadPs4Enabled)
			return;

		string? trophyDir = ShadPs4Scanner.GetShadPs4TrophyDir();
		if (trophyDir is null)
			return;

		try
		{
			foreach (string npwrDir in Directory.EnumerateDirectories(trophyDir))
			{
				string npwrId = Path.GetFileName(npwrDir);
				List<Achievement> achievements = ShadPs4Scanner.ParseNpwrDirectory(npwrId);
				if (achievements.Count == 0)
					continue;

				HashSet<string> unlocked = achievements
					.Where(a => a.IsUnlocked)
					.Select(a => a.Id)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				SetKnownUnlocks(npwrId, unlocked);
			}
		}
		catch { }
	}

	protected override void SetupFileWatchers()
	{
		if (!settings.Settings.ShadPs4Enabled)
			return;

		// Trophy definitions decide whether a game exists, progress files decide what is unlocked
		if (ShadPs4Scanner.GetShadPs4TrophyDir() is string trophyDir)
			AddFileWatcher(trophyDir, OnTrophyFileChanged);

		if (ShadPs4Scanner.GetShadPs4HomeDir() is string homeDir)
			AddFileWatcher(homeDir, OnProgressFileChanged);
	}

	private void AddFileWatcher(
		string path,
		FileSystemEventHandler handler)
	{
		try
		{
			FileSystemWatcher watcher = new(path)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.CreationTime,
				EnableRaisingEvents = true
			};

			watcher.Changed += handler;
			watcher.Created += handler;
			watcher.Deleted += handler;
			watcher.Renamed += (s, e) =>
			{
				handler(s, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath) ?? path, e.OldName));
				handler(s, e);
			};
			AddWatcher(watcher);
		}
		catch { }
	}

	private void OnTrophyFileChanged(
		object sender,
		FileSystemEventArgs e)
	{
		if (sender is not FileSystemWatcher watcher)
			return;

		// The NPWR id is the first path segment under the trophy root
		string relative = Path.GetRelativePath(watcher.Path, e.FullPath);
		string npwrId = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
		if (!string.IsNullOrEmpty(npwrId) && npwrId != "..")
			ScheduleDetection(npwrId);
	}

	private void OnProgressFileChanged(
		object sender,
		FileSystemEventArgs e)
	{
		if (!ShadPs4Scanner.IsShadPs4ProgressFile(e.FullPath))
			return;

		string? npwrId = ShadPs4Scanner.GetNpwrIdFromProgressFile(e.FullPath);
		if (npwrId is not null)
			ScheduleDetection(npwrId);
	}

	protected override Task DetectNewAchievementsAsync(string npwrId)
	{
		try
		{
			// Same definition of a game as ShadPs4Scanner: a parsable trophy folder
			List<Achievement> achievements = ShadPs4Scanner.ParseNpwrDirectory(npwrId);
			if (achievements.Count == 0)
			{
				MarkGameRemoved(npwrId);
				return Task.CompletedTask;
			}

			MarkGamePresent(npwrId);
			List<Achievement> currentUnlocked = [.. achievements.Where(a => a.IsUnlocked)];
			List<Achievement> newAchievements = DiffAndUpdate(npwrId, currentUnlocked);

			foreach (Achievement ach in newAchievements)
			{
				RaiseAchievementUnlocked(npwrId, ach);
			}
		}
		catch { }

		return Task.CompletedTask;
	}
}
