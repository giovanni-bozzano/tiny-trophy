namespace TinyTrophy.Models;

/// <summary>
/// Cached achievement schema from the Steam Web API.
/// Contains only metadata — no runtime state like unlock status.
/// </summary>
public sealed class SteamAchievementSchema
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public string IconUri { get; set; } = string.Empty;
	public string IconLockedUri { get; set; } = string.Empty;
	public bool IsHidden { get; set; }
	public double GlobalPercentage { get; set; }
}
