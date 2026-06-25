namespace TinyTrophy.Models;

public sealed class Game
{
	public string AppId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string ImageUri { get; set; } = string.Empty;
	public AchievementSource Source { get; set; }
	public List<Achievement> Achievements { get; set; } = [];
	public TimeSpan Playtime { get; set; }
	public DateTime LastPlayed { get; set; }

	public int UnlockedCount => Achievements.Count(a => a.IsUnlocked);
	public int TotalCount => Achievements.Count;
	public double CompletionPercentage => TotalCount > 0 ? (double)UnlockedCount / TotalCount * 100 : 0;
}

public enum AchievementSource
{
	SteamEmulator,
	SteamOfficial,
	ShadPs4
}
