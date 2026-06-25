namespace TinyTrophy.Models;

public sealed class Achievement
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string IconUri { get; set; } = string.Empty;
	public string IconLockedUri { get; set; } = string.Empty;
	public bool IsUnlocked { get; set; }
	public DateTime? UnlockTime { get; set; }
	public bool IsHidden { get; set; }
	public double GlobalPercentage { get; set; }
	public string TrophyType { get; set; } = string.Empty;
}
