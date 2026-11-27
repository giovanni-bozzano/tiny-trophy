using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TinyTrophy.Infrastructure;
using TinyTrophy.Models;

namespace TinyTrophy.Views;

public partial class UpdateDialog
	: Window
{
	private readonly GitHubReleaseAsset? _asset;
	private readonly string? _newVersion;
	private CancellationTokenSource? _cts;
	private bool _closed;

	public UpdateDialog()
	{
		InitializeComponent();
	}

	public UpdateDialog(GitHubRelease release)
		: this()
	{
		_asset = UpdateService.FindAsset(release);
		_newVersion = release.TagName;
		VersionText.Text = $"Version {release.TagName} is now available.";
		ReleaseLink.NavigateUri = new Uri(release.HtmlUrl);
		ReleaseLink.Content = "View release on GitHub";

		// If no downloadable exe asset, hide the update button
		if (_asset is null)
			DownloadButton.IsVisible = false;
	}

	protected override void OnClosed(EventArgs e)
	{
		_closed = true;
		// Closing the dialog must abort the download: otherwise it would finish later and replace the
		// executable even though the user dismissed the update.
		_cts?.Cancel();
		base.OnClosed(e);
	}

	private void OnSkipClicked(
		object? sender,
		RoutedEventArgs e)
	{
		_cts?.Cancel();
		Close();
	}

	private async void OnDownloadClicked(
		object? sender,
		RoutedEventArgs e)
	{
		if (_asset is null)
			return;

		// Switch UI to download mode
		DownloadButton.IsEnabled = false;
		ProgressPanel.IsVisible = true;
		StatusText.Text = "Downloading...";

		using CancellationTokenSource cts = new();
		_cts = cts;
		CancellationToken ct = cts.Token;
		Progress<double> progress = new(p =>
		{
			Dispatcher.UIThread.Post(() =>
			{
				DownloadProgress.Value = p * 100;
				StatusText.Text = $"Downloading... {p:P0}";
			});
		});

		try
		{
			string tempPath = await UpdateService.DownloadUpdateAsync(_asset, progress, ct);
			if (_closed || ct.IsCancellationRequested)
				return;

			StatusText.Text = "Installing update...";
			await Task.Delay(200, ct); // Brief pause so the user sees the status change
			if (_closed || ct.IsCancellationRequested)
				return;

			UpdateService.ApplyUpdateAndRestart(tempPath, _newVersion);
		}
		catch (OperationCanceledException)
		{
			if (!_closed)
			{
				StatusText.Text = "Download cancelled.";
				ResetUi();
			}
		}
		catch (Exception ex)
		{
			if (!_closed)
			{
				StatusText.Text = $"Update failed: {ex.Message}";
				ResetUi();
			}
		}
		finally
		{
			if (ReferenceEquals(_cts, cts))
				_cts = null;
		}
	}

	private void ResetUi()
	{
		DownloadButton.IsEnabled = true;
		DownloadProgress.Value = 0;
	}
}
