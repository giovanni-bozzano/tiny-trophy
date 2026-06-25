using Avalonia.Controls;
using TinyTrophy.ViewModels;

namespace TinyTrophy;

public partial class MainWindow
	: Window
{
	// When true, the window actually closes instead of hiding to tray
	public bool AllowClose { get; set; }

	public MainWindow()
	{
		InitializeComponent();
	}

	public MainWindow(MainViewModel viewModel)
		: this()
	{
		DataContext = viewModel;
	}

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (!AllowClose)
		{
			// Hide to tray instead of closing
			e.Cancel = true;
			Hide();
			return;
		}

		base.OnClosing(e);
	}
}
