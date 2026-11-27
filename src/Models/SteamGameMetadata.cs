namespace TinyTrophy.Models;

public sealed class SteamGameMetadata
{
	public string Name { get; set; } = string.Empty;
	public string HeaderImageUri { get; set; } = string.Empty;

	/// <summary>
	/// Language the achievement wording was fetched in, so a language change invalidates the cached copy.
	/// </summary>
	public string Language { get; set; } = string.Empty;
	public Dictionary<string, SteamAchievementSchema> Achievements { get; set; } = [];
}
