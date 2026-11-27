using TinyTrophy.Infrastructure.Scanners;
using TinyTrophy.Models;

namespace TinyTrophy.Tests;

[CollectionDefinition("CurrentDirectory", DisableParallelization = true)]
public sealed class CurrentDirectoryCollection;

[Collection("CurrentDirectory")]
public sealed class SteamEmulatorScannerTests
	: IDisposable
{
	private readonly string _originalDirectory = Directory.GetCurrentDirectory();
	private readonly TempDirectory _temp = new();

	public void Dispose()
	{
		Directory.SetCurrentDirectory(_originalDirectory);
		_temp.Dispose();
	}

	[Fact]
	public void DefaultDirectories_ArePortableEnabledAndUnique()
	{
		List<DirectoryConfig> watched = SteamEmulatorScanner.GetDefaultWatchedDirectories();
		List<DirectoryConfig> prefixes = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories();

		Assert.NotEmpty(watched);
		Assert.All(watched, AssertPortableDefault);
		Assert.Equal(
			watched.Count,
			watched.Select(d => d.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());

		Assert.NotEmpty(prefixes);
		Assert.All(prefixes, AssertPortableDefault);
		Assert.Equal(
			prefixes.Count,
			prefixes.Select(d => d.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
	}

	private static void AssertPortableDefault(DirectoryConfig directory)
	{
		Assert.True(directory.IsDefault);
		Assert.True(directory.Enabled);
		Assert.False(string.IsNullOrWhiteSpace(directory.Label));
		Assert.False(Path.IsPathFullyQualified(directory.Path));
	}

	[Fact]
	public void ExpandPath_ReplacesKnownTokens()
	{
		string? expanded = SteamEmulatorScanner.ExpandPath(
			Path.Combine("%AppData%", "Goldberg SteamEmu Saves"));

		Assert.NotNull(expanded);
		Assert.DoesNotContain("%AppData%", expanded);
		Assert.StartsWith(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			expanded,
			StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void CollapsePath_InvertsExpandPath()
	{
		string expanded = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Goldberg SteamEmu Saves");

		Assert.Equal(
			Path.Combine("%AppData%", "Goldberg SteamEmu Saves"),
			SteamEmulatorScanner.CollapsePath(expanded));
	}

	[Fact]
	public void ExpandProtonPrefixDirectoryGlob_RelativePattern_DoesNotDuplicateTheFirstSegment()
	{
		_temp.CreateDirectory(Path.Combine("prefixes", "one", "pfx"));
		_temp.CreateDirectory(Path.Combine("prefixes", "two", "pfx"));
		_temp.CreateDirectory(Path.Combine("prefixes", "three"));

		Directory.SetCurrentDirectory(_temp.Root);

		List<string> resolved = [.. SteamEmulatorScanner
			.ExpandProtonPrefixDirectoryGlob(Path.Combine("prefixes", "*", "pfx"))
			.Select(Path.GetFullPath)];

		Assert.Equal(2, resolved.Count);
		Assert.Contains(Path.GetFullPath(Path.Combine(_temp.Root, "prefixes", "one", "pfx")), resolved);
		Assert.Contains(Path.GetFullPath(Path.Combine(_temp.Root, "prefixes", "two", "pfx")), resolved);
	}

	[Fact]
	public void GetDefaultWatchedDirectories_CoverTheKnownEmulators()
	{
		List<DirectoryConfig> directories = SteamEmulatorScanner.GetDefaultWatchedDirectories();

		Assert.NotEmpty(directories);
		Assert.All(directories, d =>
		{
			Assert.True(d.IsDefault);
			Assert.False(string.IsNullOrWhiteSpace(d.Label));
			Assert.False(string.IsNullOrWhiteSpace(d.Path));
		});
		Assert.Contains(directories, d => d.Label == "Goldberg");
		Assert.Contains(directories, d => d.Label == "GSE");
	}

	[Fact]
	public void GetDefaultProtonPrefixDirectories_AreDefaults()
	{
		List<DirectoryConfig> directories = SteamEmulatorScanner.GetDefaultProtonPrefixDirectories();

		Assert.NotEmpty(directories);
		Assert.All(directories, d => Assert.True(d.IsDefault));
	}

	[Fact]
	public void GetEnabledResolvedDirectories_ReturnsOnlyExistingEnabledDirectories()
	{
		string existing = _temp.CreateDirectory("existing-source");
		string missing = Path.Combine(_temp.Root, "missing-source");

		AppSettings settings = new()
		{
			WatchedDirectories =
			[
				new DirectoryConfig { Path = existing, Label = "Existing", Enabled = true, IsDefault = false },
				new DirectoryConfig { Path = missing, Label = "Missing", Enabled = true, IsDefault = false },
				new DirectoryConfig { Path = existing, Label = "Disabled", Enabled = false, IsDefault = false }
			]
		};

		SteamEmulatorScanner.ClearResolvedDirectoriesCache();
		try
		{
			List<string> resolved = [.. SteamEmulatorScanner
				.GetEnabledResolvedDirectories(settings)
				.Select(Path.GetFullPath)];

			Assert.Single(resolved);
			Assert.Equal(Path.GetFullPath(existing), resolved[0]);
		}
		finally
		{
			SteamEmulatorScanner.ClearResolvedDirectoriesCache();
		}
	}
}
