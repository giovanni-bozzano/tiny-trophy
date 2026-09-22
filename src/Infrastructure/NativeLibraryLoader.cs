using System.Reflection;
using System.Runtime.InteropServices;

namespace TinyTrophy.Infrastructure;

/// <summary>
/// Extracts and preloads the native libraries (Skia, HarfBuzz, ANGLE) that are embedded into the
/// executable at build time, so the app ships as a single file under NativeAOT.
/// </summary>
/// <remarks>
/// NativeAOT statically links the .NET runtime and managed code, but third-party native libraries
/// remain plain DLLs that the OS loader can only load from a real path. Rather than letting them sit
/// beside the executable, they are embedded as managed resources and written here on first launch.
/// <para>
/// Extraction deliberately targets <see cref="AppPaths.CacheDir"/> instead of the temp directory the
/// single-file host used: temp cleaners were deleting those files while this long-running tray app
/// was still in use, so the first lazily-resolved P/Invoke (text shaping when showing a notification)
/// threw <see cref="DllNotFoundException"/>. Every library is additionally loaded eagerly during
/// startup, which keeps the module resident for the lifetime of the process even if the files are
/// deleted afterwards.
/// </para>
/// </remarks>
public static class NativeLibraryLoader
{
	private const string ResourcePrefix = "TinyTrophy.Native.";

	private static bool s_initialized;

	/// <summary>
	/// Extracts any embedded native libraries and loads them into the process. Must run before the
	/// first P/Invoke into them, i.e. before Avalonia is initialized.
	/// </summary>
	public static void Initialize()
	{
		if (s_initialized)
			return;
		s_initialized = true;

		Assembly assembly = typeof(NativeLibraryLoader).Assembly;
		string[] resourceNames = [.. assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))];
		if (resourceNames.Length == 0)
			return;

		// Version the directory so an updated build never loads a stale library.
		string version = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
		string rootDir = Path.Combine(AppPaths.CacheDir, "native");
		string targetDir = Path.Combine(rootDir, version);
		Directory.CreateDirectory(targetDir);

		foreach (string resourceName in resourceNames)
		{
			string fileName = resourceName[ResourcePrefix.Length..];
			string targetPath = Path.Combine(targetDir, fileName);

			Extract(assembly, resourceName, targetPath);
			NativeLibrary.Load(targetPath);
		}

		RemoveStaleVersions(rootDir, targetDir);
	}

	/// <summary>
	/// Deletes the directories left behind by previously installed versions.
	/// </summary>
	/// <remarks>
	/// Failures are ignored on purpose: an instance of an older version may still be running while its
	/// replacement starts (the updater relaunches before the old process fully exits), and the
	/// libraries it loaded stay locked until it does. Such a directory is simply cleaned up on a later
	/// launch instead.
	/// </remarks>
	private static void RemoveStaleVersions(string rootDir, string currentDir)
	{
		foreach (string directory in Directory.EnumerateDirectories(rootDir))
		{
			if (string.Equals(directory, currentDir, StringComparison.OrdinalIgnoreCase))
				continue;

			try
			{
				Directory.Delete(directory, recursive: true);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Still in use by another instance; it will be removed on a future launch.
			}
		}
	}

	/// <summary>
	/// Writes the resource to disk unless an identically sized file is already present. The content is
	/// staged under a unique temp name first so a half-written library is never observable, and so a
	/// concurrent instance extracting the same file cannot be caught mid-write.
	/// </summary>
	private static void Extract(Assembly assembly, string resourceName, string targetPath)
	{
		using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
		if (resourceStream is null)
			return;

		FileInfo existing = new(targetPath);
		if (existing.Exists && existing.Length == resourceStream.Length)
			return;

		string stagingPath = $"{targetPath}.{Environment.ProcessId}.tmp";
		try
		{
			using (FileStream stagingStream = new(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None))
				resourceStream.CopyTo(stagingStream);

			File.Move(stagingPath, targetPath, overwrite: true);
		}
		catch (IOException) when (File.Exists(targetPath))
		{
			// Another instance won the race and the library is already in place.
		}
		finally
		{
			if (File.Exists(stagingPath))
				File.Delete(stagingPath);
		}
	}
}
