using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace TinyTrophy.Services;

/// <summary>
/// A disk-cached image loader with no in-memory cache.
/// Off-screen bitmaps get garbage collected; on re-scroll, images are decoded from disk.
/// </summary>
public sealed class DiskOnlyImageLoader
	: BaseWebImageLoader
{
	private static readonly string CacheDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"TinyTrophy",
		"imagecache");

	public DiskOnlyImageLoader()
	{
		Directory.CreateDirectory(CacheDir);
	}

	protected override async Task<Bitmap?> LoadFromGlobalCache(string url)
	{
		string cachePath = GetCachePath(url);
		if (!File.Exists(cachePath))
			return null;

		try
		{
			byte[] bytes = await File.ReadAllBytesAsync(cachePath);
			using MemoryStream stream = new(bytes);
			return new Bitmap(stream);
		}
		catch
		{
			// Corrupted file — delete and let it re-download
			try
			{
				File.Delete(cachePath);
			}
			catch { }
			return null;
		}
	}

	protected override async Task SaveToGlobalCache(
		string url,
		byte[] imageBytes)
	{
		try
		{
			string cachePath = GetCachePath(url);
			await File.WriteAllBytesAsync(cachePath, imageBytes);
		}
		catch { }
	}

	private static string GetCachePath(string url)
	{
		// Use a stable hash of the URL as the filename
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
		return Path.Combine(CacheDir, hash);
	}
}
