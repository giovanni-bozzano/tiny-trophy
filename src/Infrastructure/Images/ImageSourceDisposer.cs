using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using System.Runtime.CompilerServices;

namespace TinyTrophy.Infrastructure.Images;

public sealed class ImageSourceDisposer
{
	public static readonly AttachedProperty<bool> DisposeSourceProperty =
		AvaloniaProperty.RegisterAttached<ImageSourceDisposer, Image, bool>(
			"DisposeSource",
			defaultValue: false);

	// The subscription that disposes a replaced source, held per Image so it can be released instead of
	// staying active for the life of the control.
	private static readonly ConditionalWeakTable<Image, IDisposable> s_sourceSubscriptions = new();

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
			s_sourceSubscriptions.AddOrUpdate(
				image,
				image.GetPropertyChangedObservable(Image.SourceProperty).Subscribe(new SourceChangedObserver()));
		}
		else
		{
			image.DetachedFromVisualTree -= Image_DetachedFromVisualTree;
			image.Unloaded -= Image_Unloaded;

			if (s_sourceSubscriptions.TryGetValue(image, out IDisposable? subscription))
			{
				subscription.Dispose();
				s_sourceSubscriptions.Remove(image);
			}
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
