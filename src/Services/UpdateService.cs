using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using TinyTrophy.Models;

namespace TinyTrophy.Services;

public static class UpdateService
{
	private const string GitHubOwner = "giovanni-bozzano";
	private const string GitHubRepo = "tiny-trophy";

	private static readonly HttpClient s_http = new()
	{
		Timeout = TimeSpan.FromSeconds(10),
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
	/// Downloads the release asset .exe to a temp file next to the current executable.
	/// Returns the path of the downloaded temp file.
	/// </summary>
	public static async Task<string> DownloadUpdateAsync(
		GitHubReleaseAsset asset,
		IProgress<double> progress,
		CancellationToken ct = default)
	{
		string exePath = Environment.ProcessPath
			?? throw new InvalidOperationException("Cannot determine current executable path.");
		string tempPath = exePath + ".update";

		using HttpResponseMessage response = await s_http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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
		return tempPath;
	}

	/// <summary>
	/// Swaps the running executable with the downloaded update and relaunches.
	/// </summary>
	public static void ApplyUpdateAndRestart(string downloadedExePath)
	{
		string currentExe = Environment.ProcessPath
			?? throw new InvalidOperationException("Cannot determine current executable path.");
		string oldExe = currentExe + ".old";

		// Clean up leftovers from a previous failed update
		if (File.Exists(oldExe))
			File.Delete(oldExe);

		// Swap the current exe with the downloaded update
		File.Move(currentExe, oldExe);
		File.Move(downloadedExePath, currentExe);

		// Relaunch with the new version
		Process.Start(new ProcessStartInfo(currentExe) { UseShellExecute = true });
		Environment.Exit(0);
	}

	/// <summary>
	/// Deletes leftover .old and .update files from a previous update.
	/// </summary>
	public static void CleanupPreviousUpdate()
	{
		string? currentExe = Environment.ProcessPath;
		if (currentExe is null)
			return;

		try
		{
			string oldExe = currentExe + ".old";
			if (File.Exists(oldExe))
				File.Delete(oldExe);

			string updateExe = currentExe + ".update";
			if (File.Exists(updateExe))
				File.Delete(updateExe);
		}
		catch { }
	}

	/// <summary>
	/// Finds the .exe asset from a release's asset list.
	/// </summary>
	public static GitHubReleaseAsset? FindExeAsset(GitHubRelease release)
	{
		return release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
	}

	private static Version? ParseVersion(string tag)
	{
		string trimmed = tag.TrimStart('v', 'V');
		return Version.TryParse(trimmed, out Version? version) ? version : null;
	}
}
