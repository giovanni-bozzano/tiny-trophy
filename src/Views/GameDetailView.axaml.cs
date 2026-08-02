using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Views;

public partial class GameDetailView
	: UserControl
{
	private CancellationTokenSource? _copiedPopupResetCts;

	public GameDetailView()
	{
		InitializeComponent();
		AddHandler(PointerPressedEvent, OnViewPointerPressed, handledEventsToo: false);
	}

	private void OnViewPointerPressed(
		object? sender,
		PointerPressedEventArgs e)
	{
		if (e.Source is not TextBox)
			Focus();
	}

	private async void OnAppIdPointerPressed(
		object? sender,
		PointerPressedEventArgs e)
	{
		e.Handled = true;

		if (DataContext is not GameDetailViewModel viewModel)
			return;

		TopLevel? topLevel = TopLevel.GetTopLevel(this);
		if (topLevel?.Clipboard is null)
			return;

		await topLevel.Clipboard.SetTextAsync(viewModel.GameAppId);

		// Show a "Copied!" popup as feedback.
		_copiedPopupResetCts?.Cancel();
		CancellationTokenSource cts = new();
		_copiedPopupResetCts = cts;

		CopiedPopup.IsOpen = true;
		try
		{
			await Task.Delay(1200, cts.Token);
			CopiedPopup.IsOpen = false;
		}
		catch (TaskCanceledException)
		{
			// A newer click superseded this reset; the newer one will handle it.
		}
	}
}
