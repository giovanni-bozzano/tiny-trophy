namespace TinyTrophy.Models;

public sealed class SteamGameMetadata
{
	public string Name { get; set; } = string.Empty;
	public string HeaderImageUri { get; set; } = string.Empty;
	public Dictionary<string, SteamAchievementSchema> Achievements { get; set; } = [];
}
