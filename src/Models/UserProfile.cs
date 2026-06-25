namespace TinyTrophy.Models;

public sealed class UserProfile
{
	public int TotalAchievements { get; set; }
	public int TotalGames { get; set; }
	public int PerfectGames { get; set; }
	public double OverallCompletion { get; set; }
}
