using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace TinyTrophy.Services;

/// <summary>
/// Image loader that resizes images once, right after download, and caches them on disk at that size.
/// </summary>
/// <remarks>
/// Decoded bitmaps live in unmanaged memory that the garbage collector barely accounts for, so holding on
/// to them is what makes the process grow. Nothing is held here: every bitmap handed out belongs to the
/// control that asked for it and goes away with it. Since the lists only realise the rows that are on
/// screen, that bounds what the app holds to about a screenful.
/// </remarks>
public sealed class DiskOnlyImageLoader
	: BaseWebImageLoader
{
	private static readonly string CacheDir = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"TinyTrophy",
		"imagecache");

	private const string PartialSuffix = ".partial";

	private readonly Lock _downloadsLock = new();
	private readonly Dictionary<string, Task> _downloads = [];

	// The pixel width every image is resized to and cached at once, as soon as it is downloaded.
	// Controls are drawn at a logical width, but on any display with DPI scaling (e.g. 150%/200%) the
	// physical pixel width is larger, so resizing to the logical width alone looks blurry once Avalonia
	// upscales it to fill the control. Pass roughly 2x the logical display width this loader serves so
	// the cached copy already covers HiDPI screens.
	private readonly int _targetWidth;

	public DiskOnlyImageLoader(int targetWidth)
	{
		_targetWidth = targetWidth;

		Directory.CreateDirectory(CacheDir);
		Task.Run(DeleteStaleFiles);
	}

	protected override async Task<Bitmap?> LoadAsync(string uri)
	{
		if (string.IsNullOrWhiteSpace(uri))
			return null;

		Bitmap? cached = await LoadFromGlobalCache(uri);
		if (cached is not null)
			return cached;

		await DownloadOnceAsync(uri);

		return await LoadFromGlobalCache(uri);
	}

	protected override Task<Bitmap?> LoadFromGlobalCache(string uri)
	{
		string cachePath = GetCachePath(uri);
		if (!File.Exists(cachePath))
			return Task.FromResult<Bitmap?>(null);
		try
		{
			// Already resized and stored at _targetWidth, so there is nothing to do but read it
			using FileStream stream = File.OpenRead(cachePath);
			return Task.FromResult<Bitmap?>(new Bitmap(stream));
		}
		catch
		{
			// Corrupted file — delete and let it be downloaded again
			try
			{
				File.Delete(cachePath);
			}
			catch { }

			return Task.FromResult<Bitmap?>(null);
		}
	}

	/// <summary>
	/// Resizes a downloaded image to <see cref="_targetWidth"/> and stores it at that size, so later
	/// reads are cheap file reads instead of a decode-and-downscale of a much larger original.
	/// </summary>
	protected override Task SaveToGlobalCache(
		string uri,
		byte[] imageBytes)
	{
		string cachePath = GetCachePath(uri);

		// Written beside the real file and moved into place, so a download that dies halfway through
		// cannot leave a truncated image behind for the next run to read
		string partialPath = cachePath + PartialSuffix;

		try
		{
			using MemoryStream source = new(imageBytes);
			using Bitmap bitmap = Bitmap.DecodeToWidth(source, _targetWidth);

			using (FileStream file = File.Create(partialPath))
			{
				// PNG rather than JPEG because the achievement icons rely on transparency
				bitmap.Save(file, new PngBitmapEncoderOptions());
			}

			File.Move(partialPath, cachePath, overwrite: true);
		}
		catch
		{
			try
			{
				File.Delete(partialPath);
			}
			catch { }
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Downloads an image, joining a download already running for it instead of starting a second one.
	/// </summary>
	/// <remarks>
	/// The same image usually appears in more than one place at once, and without this each of them would
	/// fetch and write the very same file.
	/// </remarks>
	private Task DownloadOnceAsync(string uri)
	{
		lock (_downloadsLock)
		{
			if (_downloads.TryGetValue(uri, out Task? running))
				return running;

			// Started on another thread so that it cannot finish, and try to remove itself, before the
			// line below has put it in
			Task download = Task.Run(() => DownloadAsync(uri));

			_downloads[uri] = download;
			return download;
		}
	}

	/// <summary>
	/// Fetches the raw bytes for a URI, reading it straight off disk when it is a <c>file://</c> URI
	/// instead of trying to download it.
	/// </summary>
	private async Task<byte[]?> LoadBytesAsync(string uri)
	{
		if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? typedUri) && typedUri.IsFile)
		{
			try
			{
				return await File.ReadAllBytesAsync(typedUri.LocalPath);
			}
			catch
			{
				return null;
			}
		}

		return await LoadDataFromExternalAsync(uri);
	}

	private async Task DownloadAsync(string uri)
	{
		try
		{
			byte[]? bytes = await LoadBytesAsync(uri);
			if (bytes is not null)
				await SaveToGlobalCache(uri, bytes);
		}
		finally
		{
			lock (_downloadsLock)
				_downloads.Remove(uri);
		}
	}

	/// <summary>
	/// Deletes images left behind by a version that stored them at a different size, along with any
	/// interrupted download.
	/// </summary>
	private static void DeleteStaleFiles()
	{
		try
		{
			foreach (string path in Directory.EnumerateFiles(CacheDir))
			{
				if (Path.GetFileName(path).EndsWith(PartialSuffix, StringComparison.Ordinal))
				{
					try
					{
						File.Delete(path);
					}
					catch { }
				}
			}
		}
		catch { }
	}

	private static string GetCachePath(string uri)
	{
		// Use a stable hash of the URI as the filename
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));
		return Path.Combine(CacheDir, hash);
	}
}
