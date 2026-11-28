using AsyncImageLoader;
using AsyncImageLoader.Core.Leases;
using AsyncImageLoader.Core.Pipeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;

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

	// One entry per Image, reached through a weak table rather than a static dictionary keyed by the
	// control: a load that never finishes can then never pin an Image and the bitmap it still shows.
	private static readonly ConditionalWeakTable<Image, LoadState> s_states = new();

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

		LoadState state = s_states.GetValue(sender, static _ => new LoadState());
		state.EnsureSubscribed(sender);

		CancellationTokenSource cts = state.Begin(out CancellationToken token);

		// A row that was realised but has not been put on screen yet is loaded when it attaches, not
		// here, so scrolling straight past it never starts a decode
		if (string.IsNullOrWhiteSpace(url) || !sender.IsAttachedToVisualTree())
		{
			state.Complete(cts);

			if (string.IsNullOrWhiteSpace(url))
				sender.Source = null;

			return;
		}

		await LoadAsync(sender, url, state, cts, token);
	}

	private static async void OnAttachedToVisualTree(
		object? sender,
		VisualTreeAttachmentEventArgs e)
	{
		if (sender is not Image image || !s_states.TryGetValue(image, out LoadState? state))
			return;

		// ImageSourceDisposer freed the previous bitmap when the control was detached, so the row has to
		// ask for it again; this is what keeps a scrolled-back row from staying blank
		string? url = GetSource(image);
		CancellationTokenSource cts = state.Begin(out CancellationToken token);
		if (string.IsNullOrWhiteSpace(url))
		{
			state.Complete(cts);
			return;
		}

		await LoadAsync(image, url, state, cts, token);
	}

	private static void OnDetachedFromVisualTree(
		object? sender,
		VisualTreeAttachmentEventArgs e)
	{
		// A decode for a row that is no longer on screen is wasted work and a bitmap nobody can show
		if (sender is Image image && s_states.TryGetValue(image, out LoadState? state))
			state.Cancel();
	}

	private static async Task LoadAsync(
		Image image,
		string url,
		LoadState state,
		CancellationTokenSource cts,
		CancellationToken token)
	{
		IAsyncImageLoader loader = GetLoader(image) ?? ImageLoader.AsyncImageLoader;

		IImage? loaded = null;
		try
		{
			// Skipped for a row that is scrolled past inside this window, before anything is read
			await Task.Delay(10, token);

			// Once the bitmap is handed to the Image, ImageSourceDisposer frees it along with the control
			IImageLease? lease = await loader.LoadAsync(new ImageLoadRequest(url), token);
			if (lease is null)
				return;

			// A newer load superseded this one while it was in flight, so it is released here instead of
			// being left for the control that will never own it
			if (token.IsCancellationRequested)
			{
				lease.Dispose();
				return;
			}

			loaded = lease.Image;
		}
		catch (OperationCanceledException)
		{
			// Superseded by a newer source, or the control left the visual tree
		}
		catch
		{
			// This runs from an async void handler, where an escaping exception would terminate the
			// process. A loader can also fail during shutdown or on an undecodable image.
		}
		finally
		{
			if (loaded is not null)
			{
				// The control can have been detached while the load was running, and a bitmap assigned to
				// it then would never be disposed: nothing fires an unload for a control that is already
				// gone, so the Image and the pixels would both stay
				if (state.IsCurrent(cts) && image.IsAttachedToVisualTree())
					image.Source = loaded;
				else
					(loaded as IDisposable)?.Dispose();
			}

			state.Complete(cts);
		}
	}

	/// <summary>
	/// The in-flight load for one <see cref="Image"/>. Holds no reference to the control, so it is
	/// collected along with it even if a load never finishes.
	/// </summary>
	private sealed class LoadState
	{
		private readonly Lock _gate = new();
		private CancellationTokenSource? _cts;
		private int _subscribed;

		/// <summary>
		/// Subscribes once, so a control that has never been attached is loaded the first time it is.
		/// </summary>
		public void EnsureSubscribed(Image image)
		{
			if (Interlocked.Exchange(ref _subscribed, 1) != 0)
				return;

			image.AttachedToVisualTree += OnAttachedToVisualTree;
			image.DetachedFromVisualTree += OnDetachedFromVisualTree;
		}

		/// <summary>
		/// Starts a load, cancelling and disposing the one it supersedes.
		/// </summary>
		public CancellationTokenSource Begin(out CancellationToken token)
		{
			CancellationTokenSource cts = new();
			token = cts.Token;

			CancellationTokenSource? previous;
			lock (_gate)
			{
				previous = _cts;
				_cts = cts;
			}

			if (previous is not null)
				CancelAndDispose(previous);

			return cts;
		}

		public bool IsCurrent(CancellationTokenSource cts)
		{
			lock (_gate)
				return ReferenceEquals(_cts, cts);
		}

		/// <summary>
		/// Stops the load in flight, without starting another one.
		/// </summary>
		public void Cancel()
		{
			CancellationTokenSource? previous;
			lock (_gate)
			{
				previous = _cts;
				_cts = null;
			}

			if (previous is not null)
				CancelAndDispose(previous);
		}

		/// <summary>
		/// Drops the cancellation source once its load has finished, so it is not held until the next
		/// source change.
		/// </summary>
		public void Complete(CancellationTokenSource cts)
		{
			lock (_gate)
			{
				if (!ReferenceEquals(_cts, cts))
					return;

				_cts = null;
			}

			cts.Dispose();
		}

		private static void CancelAndDispose(CancellationTokenSource cts)
		{
			try
			{
				cts.Cancel();
			}
			catch
			{
				// Cancellation callbacks are external code and must not break the next load
			}
			finally
			{
				cts.Dispose();
			}
		}
	}
}
