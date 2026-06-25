using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TinyTrophy.Models;
using TinyTrophy.Services;

namespace TinyTrophy.Views;

public partial class UpdateDialog
	: Window
{
	private readonly GitHubReleaseAsset? _asset;
	private CancellationTokenSource? _cts;

	public UpdateDialog()
	{
		InitializeComponent();
	}

	public UpdateDialog(GitHubRelease release)
		: this()
	{
		_asset = UpdateService.FindExeAsset(release);
		VersionText.Text = $"Version {release.TagName} is now available.";
		ReleaseLink.NavigateUri = new Uri(release.HtmlUrl);
		ReleaseLink.Content = "View release on GitHub";

		// If no downloadable exe asset, hide the update button
		if (_asset is null)
			DownloadButton.IsVisible = false;
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

		_cts = new CancellationTokenSource();
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
			string tempPath = await UpdateService.DownloadUpdateAsync(_asset, progress, _cts.Token);

			StatusText.Text = "Installing update...";
			await Task.Delay(200); // Brief pause so the user sees the status change

			UpdateService.ApplyUpdateAndRestart(tempPath);
		}
		catch (OperationCanceledException)
		{
			StatusText.Text = "Download cancelled.";
			ResetUi();
		}
		catch (Exception ex)
		{
			StatusText.Text = $"Update failed: {ex.Message}";
			ResetUi();
		}
	}

	private void ResetUi()
	{
		DownloadButton.IsEnabled = true;
		DownloadProgress.Value = 0;
	}
}
