namespace TinyTrophy.Infrastructure.Images;

/// <summary>
/// Holds the <see cref="CachedImageLoader"/> instances used across the app, one per display size.
/// </summary>
/// <remarks>
/// Each loader resizes whatever it downloads to its own fixed width, right after downloading, and caches
/// the result at that size.
/// </remarks>
public static class ImageLoaders
{
	// Game covers are displayed at 160px wide; cache at 2x that for HiDPI screens.
	public static readonly CachedImageLoader Default = new(160 * 2);

	// Achievement icons are displayed at 60px wide; cache at 2x that for HiDPI screens.
	public static readonly CachedImageLoader Icon = new(60 * 2);

	public static void DisposeAll()
	{
		Default.Dispose();
		Icon.Dispose();
	}
}
