using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Infrastructure;

public static class UpdateService
{
	private const string GitHubOwner = "giovanni-bozzano";
	private const string GitHubRepo = "tiny-trophy";

	private static readonly string UpdatedFlagPath = Path.Combine(AppPaths.DataDir, "updated.flag");

	private const string WindowsAssetSuffix = "windows_amd64.exe";
	private const string LinuxAssetSuffix = "linux_amd64";

	/// <summary>
	/// Writes a flag file so the next launch knows an update just happened.
	/// </summary>
	public static void WriteUpdatedFlag(string newVersion)
	{
		if (string.IsNullOrWhiteSpace(newVersion))
			return;

		try
		{
			Directory.CreateDirectory(AppPaths.DataDir);
			File.WriteAllText(UpdatedFlagPath, newVersion);
		}
		catch { }
	}

	/// <summary>
	/// Reads and deletes the flag file. Returns the version string if the flag existed, otherwise null.
	/// </summary>
	public static string? ConsumeUpdatedFlag()
	{
		try
		{
			if (!File.Exists(UpdatedFlagPath))
				return null;

			string version = File.ReadAllText(UpdatedFlagPath).Trim();
			File.Delete(UpdatedFlagPath);
			return string.IsNullOrWhiteSpace(version) ? null : version;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Used for the small release metadata request only.
	/// </summary>
	private static readonly HttpClient s_http = new()
	{
		Timeout = TimeSpan.FromSeconds(10),
		DefaultRequestHeaders = { { "User-Agent", "TinyTrophy" } }
	};

	/// <summary>
	/// Download-only client: a <see cref="HttpClient.Timeout"/> covers reading the response body, so the
	/// ten second timeout above would abort any transfer slower than that. The transfer is bounded by the
	/// caller's cancellation token instead.
	/// </summary>
	private static readonly HttpClient s_downloadHttp = new()
	{
		Timeout = Timeout.InfiniteTimeSpan,
		DefaultRequestHeaders = { { "User-Agent", "TinyTrophy" } }
	};

	/// <summary>
	/// Checks the latest GitHub release. Returns the release info if a newer version is available, otherwise null.
	/// </summary>
	public static async Task<GitHubRelease?> CheckForUpdateAsync()
	{
		try
		{
			string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
			using HttpResponseMessage response = await s_http.GetAsync(url);
			if (!response.IsSuccessStatusCode)
				return null;

			string json = await response.Content.ReadAsStringAsync();
			GitHubRelease? release = JsonSerializer.Deserialize(json, AppJsonContext.Default.GitHubRelease);
			if (release is null || string.IsNullOrWhiteSpace(release.TagName))
				return null;

			Version? remoteVersion = ParseVersion(release.TagName);
			if (remoteVersion is null)
				return null;

			Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
			if (currentVersion is null)
				return null;

			return remoteVersion > currentVersion ? release : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Downloads the release asset binary to a temp file next to the current executable.
	/// Returns the path of the downloaded temp file.
	/// </summary>
	public static async Task<string> DownloadUpdateAsync(
		GitHubReleaseAsset asset,
		IProgress<double> progress,
		CancellationToken ct = default)
	{
		string binaryPath = Environment.ProcessPath
			?? throw new InvalidOperationException("Cannot determine current executable path.");
		string tempPath = binaryPath + ".update";

		using HttpResponseMessage response = await s_downloadHttp.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
		response.EnsureSuccessStatusCode();

		long totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
		long bytesRead = 0;

		await using Stream contentStream = await response.Content.ReadAsStreamAsync(ct);
		await using FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

		byte[] buffer = new byte[81920];
		int read;
		while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
		{
			await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
			bytesRead += read;
			if (totalBytes > 0)
				progress.Report((double)bytesRead / totalBytes);
		}

		progress.Report(1.0);

		// An HTTP download has no file mode: on Unix the new file is created 0644, so after the swap it
		// would replace the running binary but fail to relaunch with a permission error.
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(
				tempPath,
				UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
				UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
				UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
		}

		return tempPath;
	}

	/// <summary>
	/// Swaps the running executable with the downloaded update and relaunches.
	/// </summary>
	public static void ApplyUpdateAndRestart(
		string downloadedExePath,
		string? newVersion = null)
	{
		string currentBinary = Environment.ProcessPath
			?? throw new InvalidOperationException("Cannot determine current binary path.");
		string oldBinary = currentBinary + ".old";

		InstallBinary(currentBinary, downloadedExePath);

		try
		{
			// Leave a flag so the next launch can show a "just updated" notice. It records the version being
			// installed, not the one this process was built as, which is about to be replaced.
			WriteUpdatedFlag(newVersion ?? string.Empty);

			// Drop the single-instance lock first: the new process starts before this one exits and would
			// otherwise see the lock still held and quit immediately.
			SingleInstance.Release();

			// Relaunch with the new version
			Process.Start(new ProcessStartInfo(currentBinary) { UseShellExecute = true });
		}
		catch
		{
			// The installed binary could not be started, so put the previous one back instead of leaving
			// the user without a runnable executable.
			RestorePreviousBinary(currentBinary, oldBinary);
			throw;
		}

		Environment.Exit(0);
	}

	/// <summary>
	/// Moves the running binary aside and installs <paramref name="downloadedExePath"/> at its path.
	/// The previous binary is kept as <c>.old</c>; if the installation fails, it is moved back so the
	/// current path is never left empty.
	/// </summary>
	internal static void InstallBinary(
		string currentBinary,
		string downloadedExePath)
	{
		string oldBinary = currentBinary + ".old";

		// A previous attempt may have failed after moving the only executable aside. Recover it first.
		if (!File.Exists(currentBinary) && File.Exists(oldBinary))
			File.Move(oldBinary, currentBinary);

		// The current binary is intact at this point, so an old backup can be replaced safely.
		if (File.Exists(oldBinary))
			File.Delete(oldBinary);

		File.Move(currentBinary, oldBinary);
		try
		{
			File.Move(downloadedExePath, currentBinary);
		}
		catch
		{
			try
			{
				if (!File.Exists(currentBinary) && File.Exists(oldBinary))
					File.Move(oldBinary, currentBinary);
			}
			catch { }

			throw;
		}
	}

	private static void RestorePreviousBinary(
		string currentBinary,
		string oldBinary)
	{
		try
		{
			if (File.Exists(currentBinary))
				File.Delete(currentBinary);

			if (File.Exists(oldBinary))
				File.Move(oldBinary, currentBinary);
		}
		catch { }
	}

	/// <summary>
	/// Deletes leftover .old and .update files from a previous update.
	/// </summary>
	public static void CleanupPreviousUpdate()
	{
		string? currentBinary = Environment.ProcessPath;
		if (currentBinary is null)
			return;

		try
		{
			string oldBinary = currentBinary + ".old";
			if (File.Exists(oldBinary))
				File.Delete(oldBinary);

			string updateBinary = currentBinary + ".update";
			if (File.Exists(updateBinary))
				File.Delete(updateBinary);
		}
		catch { }
	}

	/// <summary>
	/// Finds the correct asset from a release's asset list.
	/// </summary>
	public static GitHubReleaseAsset? FindAsset(GitHubRelease release)
	{
		string suffix = OperatingSystem.IsWindows() ? WindowsAssetSuffix : LinuxAssetSuffix;

		// Prefer the exact name the release workflow publishes
		GitHubReleaseAsset? exact = release.Assets.Find(a =>
			a.Name.Equals($"tinytrophy_{suffix}", StringComparison.OrdinalIgnoreCase));
		if (exact is not null)
			return exact;

		// Otherwise accept any asset named after the platform, but never a packaged archive
		return release.Assets.Find(a =>
			a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
			&& !a.Name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)
			&& !a.Name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)
			&& !a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
			&& !a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));
	}

	internal static Version? ParseVersion(string tag)
	{
		string trimmed = tag.TrimStart('v', 'V');
		return Version.TryParse(trimmed, out Version? version) ? version : null;
	}
}
