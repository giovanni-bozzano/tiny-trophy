using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class UpdateServiceTests
{
	private static GitHubReleaseAsset Asset(string name) => new()
	{
		Name = name,
		BrowserDownloadUrl = $"https://example.invalid/{name}",
		Size = 1
	};

	[Fact]
	public void FindAsset_PrefersTheExactPlatformBinary()
	{
		GitHubRelease release = new()
		{
			Assets =
			[
				Asset("tinytrophy_linux_amd64.deb"),
				Asset("tinytrophy_linux_amd64"),
				Asset("tinytrophy_windows_amd64.exe"),
				Asset("checksums.txt")
			]
		};

		GitHubReleaseAsset asset = Assert.IsType<GitHubReleaseAsset>(UpdateService.FindAsset(release));

		Assert.Equal(
			OperatingSystem.IsWindows() ? "tinytrophy_windows_amd64.exe" : "tinytrophy_linux_amd64",
			asset.Name);
	}

	[Fact]
	public void FindAsset_WhenOnlyPackagedArchivesExist_ReturnsNull()
	{
		GitHubRelease release = new()
		{
			Assets =
			[
				Asset("tinytrophy_linux_amd64.deb"),
				Asset("tinytrophy_linux_amd64.zip"),
				Asset("tinytrophy_windows_amd64.exe.zip")
			]
		};

		Assert.Null(UpdateService.FindAsset(release));
	}

	[Fact]
	public void FindAsset_WithoutMatchingPlatform_ReturnsNull()
	{
		GitHubRelease release = new() { Assets = [Asset("tinytrophy_macos_arm64")] };

		Assert.Null(UpdateService.FindAsset(release));
	}

	[Fact]
	public void InstallBinary_ReplacesTheCurrentBinaryAndKeepsABackup()
	{
		using TempDirectory temp = new();
		string current = Path.Combine(temp.Root, "TinyTrophy.exe");
		string downloaded = current + ".update";
		File.WriteAllText(current, "old");
		File.WriteAllText(downloaded, "new");

		UpdateService.InstallBinary(current, downloaded);

		Assert.Equal("new", File.ReadAllText(current));
		Assert.Equal("old", File.ReadAllText(current + ".old"));
		Assert.False(File.Exists(downloaded));
	}

	[Fact]
	public void InstallBinary_WhenTheDownloadIsMissing_RestoresTheCurrentBinary()
	{
		using TempDirectory temp = new();
		string current = Path.Combine(temp.Root, "TinyTrophy.exe");
		File.WriteAllText(current, "old");

		Assert.Throws<FileNotFoundException>(() =>
			UpdateService.InstallBinary(current, current + ".update"));

		Assert.Equal("old", File.ReadAllText(current));
		Assert.False(File.Exists(current + ".old"));
	}

	[Fact]
	public void InstallBinary_RecoversABackupWhenTheCurrentBinaryIsMissing()
	{
		using TempDirectory temp = new();
		string current = Path.Combine(temp.Root, "TinyTrophy.exe");
		string downloaded = current + ".update";
		File.WriteAllText(current + ".old", "old");
		File.WriteAllText(downloaded, "new");

		UpdateService.InstallBinary(current, downloaded);

		Assert.Equal("new", File.ReadAllText(current));
		Assert.Equal("old", File.ReadAllText(current + ".old"));
	}

	[Theory]
	[InlineData("v1.0.12", 1, 0, 12)]
	[InlineData("V2.3.4", 2, 3, 4)]
	[InlineData("1.2.3.4", 1, 2, 3)]
	public void ParseVersion_ReadsReleaseTags(
		string tag,
		int major,
		int minor,
		int build)
	{
		Version? version = UpdateService.ParseVersion(tag);

		Assert.NotNull(version);
		Assert.Equal(major, version.Major);
		Assert.Equal(minor, version.Minor);
		Assert.Equal(build, version.Build);
	}

	[Theory]
	[InlineData("nightly")]
	[InlineData("")]
	public void ParseVersion_RejectsNonVersions(string tag)
	{
		Assert.Null(UpdateService.ParseVersion(tag));
	}

	[Fact]
	public void ParseVersion_ComparesComponentsNumerically()
	{
		Version older = UpdateService.ParseVersion("v1.9.0")!;
		Version newer = UpdateService.ParseVersion("v1.10.0")!;
		Version muchNewer = UpdateService.ParseVersion("v2.0.0")!;

		Assert.True(newer > older);
		Assert.True(muchNewer > newer);
		Assert.False(older > newer);
	}

	[Fact]
	public void ParseVersion_TreatsThePrefixAsOptional()
	{
		Assert.Equal(UpdateService.ParseVersion("1.2.3"), UpdateService.ParseVersion("v1.2.3"));
		Assert.Equal(UpdateService.ParseVersion("1.2.3"), UpdateService.ParseVersion("V1.2.3"));
	}
}
