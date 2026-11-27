using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class GitHubReleaseTests
{
	[Fact]
	public void Deserialize_ReadsTheFieldsTheUpdaterUses()
	{
		const string json = """
			{
				"tag_name": "v1.2.3",
				"name": "Tiny Trophy 1.2.3",
				"html_url": "https://example.invalid/releases/v1.2.3",
				"assets": [
					{
						"name": "tinytrophy_windows_amd64.exe",
						"browser_download_url": "https://example.invalid/download/win",
						"size": 123456
					}
				]
			}
			""";

		GitHubRelease release = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubRelease)!;

		Assert.Equal("v1.2.3", release.TagName);
		Assert.Equal("Tiny Trophy 1.2.3", release.Name);
		Assert.Equal("https://example.invalid/releases/v1.2.3", release.HtmlUrl);

		GitHubReleaseAsset asset = Assert.Single(release.Assets);
		Assert.Equal("tinytrophy_windows_amd64.exe", asset.Name);
		Assert.Equal("https://example.invalid/download/win", asset.BrowserDownloadUrl);
		Assert.Equal(123456, asset.Size);
	}

	[Fact]
	public void Deserialize_WithoutOptionalFields_UsesEmptyDefaults()
	{
		GitHubRelease release = JsonSerializer.Deserialize(
			"""{"tag_name":"1.0.0"}""",
			AppJsonContext.Default.GitHubRelease)!;

		Assert.Equal("1.0.0", release.TagName);
		Assert.Equal(string.Empty, release.Name);
		Assert.Equal(string.Empty, release.HtmlUrl);
		Assert.Empty(release.Assets);
	}
}
