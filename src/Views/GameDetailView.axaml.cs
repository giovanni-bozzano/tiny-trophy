using Avalonia.Controls;
using Avalonia.Input;

namespace TinyTrophy.Views;

public partial class GameDetailView
	: UserControl
{
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
}
