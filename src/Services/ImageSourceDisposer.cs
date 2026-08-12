using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace TinyTrophy.Services;

public sealed class ImageSourceDisposer
{
	public static readonly AttachedProperty<bool> DisposeSourceProperty =
		AvaloniaProperty.RegisterAttached<ImageSourceDisposer, Image, bool>(
			"DisposeSource",
			defaultValue: false);

	public static bool GetDisposeSource(Image element) => element.GetValue(DisposeSourceProperty);
	public static void SetDisposeSource(Image element, bool value) => element.SetValue(DisposeSourceProperty, value);

	static ImageSourceDisposer()
	{
		DisposeSourceProperty.Changed.AddClassHandler<Image>(OnDisposeSourceChanged);
	}

	private static void OnDisposeSourceChanged(
		Image image,
		AvaloniaPropertyChangedEventArgs e)
	{
		if (e.NewValue is true)
		{
			image.DetachedFromVisualTree += Image_DetachedFromVisualTree;
			image.Unloaded += Image_Unloaded;
			image.GetPropertyChangedObservable(Image.SourceProperty).Subscribe(new SourceChangedObserver());
		}
		else
		{
			image.DetachedFromVisualTree -= Image_DetachedFromVisualTree;
			image.Unloaded -= Image_Unloaded;
		}
	}

	private sealed class SourceChangedObserver
		: IObserver<AvaloniaPropertyChangedEventArgs>
	{
		public void OnCompleted() { }
		public void OnError(Exception error) { }
		public void OnNext(AvaloniaPropertyChangedEventArgs value)
		{
			if (value.OldValue is Bitmap oldBitmap)
				oldBitmap.Dispose();
		}
	}

	private static void Image_Unloaded(
		object? sender,
		RoutedEventArgs e)
	{
		if (sender is Image image && image.Source is Bitmap bitmap)
		{
			bitmap.Dispose();
			image.Source = null;
		}
	}

	private static void Image_DetachedFromVisualTree(
		object? sender,
		VisualTreeAttachmentEventArgs e)
	{
		if (sender is Image image && image.Source is Bitmap bitmap)
		{
			bitmap.Dispose();
			image.Source = null;
		}
	}
}
