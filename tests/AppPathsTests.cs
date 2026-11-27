using TinyTrophy.Infrastructure;

namespace TinyTrophy.Tests;

public sealed class AppPathsTests
{
	[Fact]
	public void Paths_AreAbsoluteAndUnderTheAppName()
	{
		Assert.True(Path.IsPathFullyQualified(AppPaths.DataRootDir));
		Assert.Equal(Path.Combine(AppPaths.DataRootDir, "tinytrophy"), AppPaths.DataDir);
		Assert.Equal("tinytrophy", Path.GetFileName(AppPaths.CacheDir));
		Assert.True(Path.IsPathFullyQualified(AppPaths.DataDir));
		Assert.True(Path.IsPathFullyQualified(AppPaths.CacheDir));
	}

	[Fact]
	public void DataAndCache_AreDistinctRoots()
	{
		Assert.NotEqual(AppPaths.DataDir, AppPaths.CacheDir);
	}
}
