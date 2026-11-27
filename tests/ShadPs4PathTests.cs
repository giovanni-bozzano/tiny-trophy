using System.Xml.Linq;
using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

public sealed class ShadPs4PathTests
{
	private static string ProgressPath(
		string user = "user1",
		string npwr = "NPWR12345_00",
		string extension = ".xml") =>
		Path.Combine("data", "shadPS4", "home", user, "trophy", npwr + extension);

	[Fact]
	public void IsShadPs4ProgressFile_AcceptsAUserTrophyXml()
	{
		Assert.True(ShadPs4Scanner.IsShadPs4ProgressFile(ProgressPath()));
	}

	[Fact]
	public void IsShadPs4ProgressFile_RejectsAnotherExtension()
	{
		Assert.False(ShadPs4Scanner.IsShadPs4ProgressFile(ProgressPath(extension: ".xml.bak")));
		Assert.False(ShadPs4Scanner.IsShadPs4ProgressFile(ProgressPath(extension: ".json")));
	}

	[Fact]
	public void IsShadPs4ProgressFile_RequiresATrophyDirectory()
	{
		string path = Path.Combine("data", "shadPS4", "home", "user1", "trophies", "NPWR12345_00.xml");

		Assert.False(ShadPs4Scanner.IsShadPs4ProgressFile(path));
	}

	[Fact]
	public void IsShadPs4ProgressFile_RequiresTheShadPs4HomeFolder()
	{
		string path = Path.Combine("data", "other", "home", "user1", "trophy", "NPWR12345_00.xml");

		Assert.False(ShadPs4Scanner.IsShadPs4ProgressFile(path));
	}

	[Fact]
	public void IsShadPs4ProgressFile_ComparesCaseInsensitively()
	{
		string path = Path.Combine("data", "SHADPS4", "HOME", "user1", "TROPHY", "npwr12345_00.XML");

		Assert.True(ShadPs4Scanner.IsShadPs4ProgressFile(path));
	}

	[Fact]
	public async Task Scanner_WhenDisabled_ReturnsNothing()
	{
		ShadPs4Scanner scanner = new(new FakeSettingsService
		{
			Settings = new AppSettings { ShadPs4Enabled = false }
		});

		Assert.Empty(await scanner.ParseAsync());
		Assert.Empty(await scanner.ParseGameAsync("NPWR12345_00"));
	}

	[Fact]
	public void ResolveTitle_WithAnEmptyTitleName_UsesTheProductId()
	{
		XDocument document = XDocument.Parse(
			"<trophy><title-name>  </title-name><npcommid>UP0001-CUSA00001_00</npcommid></trophy>");

		Assert.Equal("UP0001-CUSA00001_00", ShadPs4Scanner.ResolveTitle(document, "NPWR12345_00"));
	}

	[Fact]
	public void ResolveTitle_WithoutAnyName_UsesTheNpwrId()
	{
		XDocument document = XDocument.Parse("<trophy><title-name></title-name></trophy>");

		Assert.Equal("NPWR12345_00", ShadPs4Scanner.ResolveTitle(document, "NPWR12345_00"));
	}

	[Fact]
	public void ResolveTitle_TrimsTheDisplayTitle()
	{
		XDocument document = XDocument.Parse("<trophy><title-name>  My Game  </title-name></trophy>");

		Assert.Equal("My Game", ShadPs4Scanner.ResolveTitle(document, "NPWR12345_00"));
	}

	[Fact]
	public void GetNpwrIdFromProgressFile_ReturnsTheFileName()
	{
		Assert.Equal("NPWR12345_00", ShadPs4Scanner.GetNpwrIdFromProgressFile(ProgressPath()));
	}

	[Fact]
	public void GetNpwrIdFromProgressFile_ForAnotherFile_ReturnsNull()
	{
		Assert.Null(ShadPs4Scanner.GetNpwrIdFromProgressFile(
			Path.Combine("data", "shadPS4", "trophy", "NPWR12345_00", "TROP_01.xml")));
	}
}
