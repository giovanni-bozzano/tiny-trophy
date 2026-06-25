using System.Text.Json.Serialization;

namespace TinyTrophy.Models;

public sealed class GitHubRelease
{
	[JsonPropertyName("tag_name")]
	public string TagName { get; set; } = string.Empty;

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("html_url")]
	public string HtmlUrl { get; set; } = string.Empty;

	[JsonPropertyName("assets")]
	public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

public sealed class GitHubReleaseAsset
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("browser_download_url")]
	public string BrowserDownloadUrl { get; set; } = string.Empty;

	[JsonPropertyName("size")]
	public long Size { get; set; }
}
