using Avalonia.Controls;
using Avalonia.Input;
using TinyTrophy.ViewModels;

namespace TinyTrophy.Views;

public partial class HomeView
	: UserControl
{
	public HomeView()
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

	private void GameCard_PointerPressed(
		object? sender,
		PointerPressedEventArgs e)
	{
		if (sender is Border border && border.DataContext is GameItemViewModel gameVm)
		{
			if (DataContext is HomeViewModel homeVm)
				homeVm.OpenGameCommand.Execute(gameVm);
		}
	}
}
