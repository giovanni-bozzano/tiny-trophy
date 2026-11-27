using AsyncImageLoader;
using AsyncImageLoader.Core.Leases;
using AsyncImageLoader.Core.Pipeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Collections.Concurrent;

namespace TinyTrophy.Infrastructure.Images;

/// <summary>
/// Attached properties that work like <see cref="ImageLoader"/>'s Source property, but
/// additionally let each <see cref="Image"/> pick which <see cref="IAsyncImageLoader"/> resolves it.
/// </summary>
/// <remarks>
/// Used instead of the bundled <c>AdvancedImage</c> control because that one is a <c>ContentControl</c>,
/// not an <see cref="Image"/>, so it is not compatible with <see cref="ImageSourceDisposer"/> and does not
/// dispose the bitmap it replaces.
/// </remarks>
public static class SizedImageLoader
{
	public static readonly AttachedProperty<string?> SourceProperty =
		AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(SizedImageLoader));

	public static readonly AttachedProperty<IAsyncImageLoader?> LoaderProperty =
		AvaloniaProperty.RegisterAttached<Image, IAsyncImageLoader?>("Loader", typeof(SizedImageLoader));

	private static readonly ConcurrentDictionary<Image, CancellationTokenSource> s_pendingOperations = new();

	static SizedImageLoader()
	{
		SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
	}

	public static string? GetSource(Image element) => element.GetValue(SourceProperty);
	public static void SetSource(Image element, string? value) => element.SetValue(SourceProperty, value);

	public static IAsyncImageLoader? GetLoader(Image element) => element.GetValue(LoaderProperty);
	public static void SetLoader(Image element, IAsyncImageLoader? value) => element.SetValue(LoaderProperty, value);

	private static async void OnSourceChanged(
		Image sender,
		AvaloniaPropertyChangedEventArgs args)
	{
		string? url = args.GetNewValue<string?>();

		CancellationTokenSource cts = s_pendingOperations.AddOrUpdate(
			sender,
			new CancellationTokenSource(),
			(_, existing) =>
			{
				existing.Cancel();
				return new CancellationTokenSource();
			});

		if (string.IsNullOrWhiteSpace(url))
		{
			s_pendingOperations.TryRemove(new KeyValuePair<Image, CancellationTokenSource>(sender, cts));
			sender.Source = null;
			return;
		}

		IAsyncImageLoader loader = GetLoader(sender) ?? ImageLoader.AsyncImageLoader;

		IImage? bitmap = await Task.Run(async () =>
		{
			try
			{
				await Task.Delay(10, cts.Token);

				// Once the bitmap is handed to the Image, ImageSourceDisposer frees it along with the control
				IImageLease? lease = await loader.LoadAsync(new ImageLoadRequest(url), cts.Token);
				if (lease is null)
					return null;

				// A newer load superseded this one while it was in flight, so it is released here instead of
				// being left for the control that will never own it
				if (cts.Token.IsCancellationRequested)
				{
					lease.Dispose();
					return null;
				}

				return lease.Image;
			}
			catch (OperationCanceledException)
			{
				return null;
			}
			catch
			{
				// This runs from an async void handler, where an escaping exception would terminate the
				// process. A loader can also fail during shutdown or on an undecodable image.
				return null;
			}
		});

		if (bitmap is not null)
		{
			if (cts.Token.IsCancellationRequested)
				(bitmap as IDisposable)?.Dispose();
			else
				sender.Source = bitmap;
		}

		s_pendingOperations.TryRemove(new KeyValuePair<Image, CancellationTokenSource>(sender, cts));
	}
}
