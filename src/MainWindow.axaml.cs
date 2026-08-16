using Avalonia.Controls;
using Avalonia.Threading;
using TinyTrophy.Infrastructure;
using TinyTrophy.Infrastructure.Images;
using TinyTrophy.ViewModels;

namespace TinyTrophy;

public partial class MainWindow
	: Window
{
	private ISettingsService? _settingsService;

	// When true, the window actually closes instead of hiding to tray
	public bool AllowClose { get; set; }

	// Raised when the window should close and "Close to tray" is disabled, so the app can perform a
	// full shutdown (tray icon disposal, service cleanup) instead of just closing this window.
	public event EventHandler? ExitRequested;

	public MainWindow()
	{
		InitializeComponent();
	}

	public MainWindow(
		MainViewModel viewModel,
		ISettingsService settingsService)
		: this()
	{
		DataContext = viewModel;
		_settingsService = settingsService;
	}

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (!AllowClose)
		{
			if (_settingsService?.Settings.CloseToTray ?? true)
			{
				// Hide to tray instead of closing
				e.Cancel = true;
				HideAndReleaseMemory();
				return;
			}

			// "Close to tray" is disabled: closing the window should fully exit the app instead of
			// leaving it running invisibly (ShutdownMode is OnExplicitShutdown)
			e.Cancel = true;
			ExitRequested?.Invoke(this, EventArgs.Empty);
			return;
		}

		base.OnClosing(e);
	}

	/// <summary>
	/// Hides the window and suspends the current view so its images are unloaded (and disposed by
	/// <see cref="ImageSourceDisposer"/>), then trims the freed memory back to the OS.
	/// </summary>
	private void HideAndReleaseMemory()
	{
		Hide();

		if (DataContext is MainViewModel viewModel)
			viewModel.SuspendView();

		// Give the dispatcher a chance to actually detach the view before collecting/trimming
		Dispatcher.UIThread.Post(MemoryTrimmer.CollectAndTrim, DispatcherPriority.Background);
	}

	/// <summary>
	/// Hands the view that was suspended while hidden back to the view model, so the window shows the
	/// same screen it did before being hidden.
	/// </summary>
	public void RestoreViewIfNeeded()
	{
		if (DataContext is MainViewModel viewModel)
			viewModel.ResumeView();
	}
}
