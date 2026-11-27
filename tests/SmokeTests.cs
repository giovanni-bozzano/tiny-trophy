using TinyTrophy.Infrastructure;

namespace TinyTrophy.Tests;

public sealed class SmokeTests
{
	[Fact]
	public void AppPaths_ResolveToNonEmptyDirectories()
	{
		Assert.False(string.IsNullOrWhiteSpace(AppPaths.DataDir));
		Assert.False(string.IsNullOrWhiteSpace(AppPaths.CacheDir));
	}
}
