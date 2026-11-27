using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using TinyTrophy.Models;

namespace TinyTrophy.Views;

public partial class AchievementNotificationWindow
	: Window
{
	private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(300);
	private static readonly TimeSpan FadeStep = TimeSpan.FromMilliseconds(16);
	private const int ScreenMargin = 12;
	private const int StackGap = 4;
	private const int MaxVisible = 3;
	private const int MaxQueued = 10;
	private static readonly TimeSpan SoundThrottle = TimeSpan.FromSeconds(2);

	// Active notifications ordered oldest to newest (index 0 is therefore the topmost on screen).
	private static readonly List<AchievementNotificationWindow> s_activeNotifications = [];
	private static readonly Queue<Achievement> s_pendingQueue = new();
	private static DateTime s_lastSoundPlayed = DateTime.MinValue;
	private static bool s_shuttingDown;

	/// <summary>
	/// Raised once no notification is showing or queued anymore. Used to release the memory a
	/// notification allocates (icon bitmap, window, animation state) when the app is sitting in the tray.
	/// </summary>
	public static event Action? AllNotificationsClosed;

	private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
	private static byte[]? s_notificationSoundData;

	// Set on the UI thread when the window closes, so a late icon load knows not to assign to it
	private bool _closed;

	public AchievementNotificationWindow()
	{
		InitializeComponent();
	}

	private AchievementNotificationWindow(Achievement achievement)
		: this()
	{
		AchievementName.Text = !string.IsNullOrEmpty(achievement.Name) ? achievement.Name : achievement.Id;
		AchievementDescription.Text = achievement.Description;

		if (!string.IsNullOrEmpty(achievement.IconUri))
			_ = LoadIconAsync(achievement.IconUri);
	}

	/// <summary>
	/// Enqueues a notification. Shows it immediately if under the max visible limit,
	/// otherwise queues it for display when a current notification closes.
	/// Must be called on the UI thread.
	/// </summary>
	public static void ShowNotification(
		Achievement achievement,
		bool playSound = false)
	{
		if (s_shuttingDown)
			return;

		if (playSound && DateTime.UtcNow - s_lastSoundPlayed > SoundThrottle)
		{
			s_lastSoundPlayed = DateTime.UtcNow;
			PlayNotificationSound();
		}

		if (s_activeNotifications.Count < MaxVisible)
		{
			AchievementNotificationWindow popup = new(achievement);
			popup.Show();
		}
		else if (s_pendingQueue.Count < MaxQueued)
		{
			s_pendingQueue.Enqueue(achievement);
		}
		// Beyond the cap the achievement is still listed in the app; showing a minutes-long backlog of
		// popups for a mass unlock is worse than dropping the extra notifications.
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		s_activeNotifications.Add(this);
		RepositionAll();

		MakeTopmost();
		_ = AnimateLifecycleAsync();
	}

	protected override void OnClosed(EventArgs e)
	{
		_closed = true;

		(AchievementIcon.Source as Bitmap)?.Dispose();
		AchievementIcon.Source = null;

		s_activeNotifications.Remove(this);
		RepositionAll();
		base.OnClosed(e);

		if (s_shuttingDown)
			return;

		// Show next queued notification if any
		if (s_pendingQueue.Count > 0 && s_activeNotifications.Count < MaxVisible)
		{
			Achievement next = s_pendingQueue.Dequeue();
			AchievementNotificationWindow popup = new(next);
			popup.Show();
		}
		else if (s_activeNotifications.Count == 0)
		{
			AllNotificationsClosed?.Invoke();
		}
	}

	/// <summary>
	/// Stops showing notifications and closes the ones on screen. Called before shutdown so a queued popup
	/// cannot open a new window while the application is closing.
	/// </summary>
	public static void Shutdown()
	{
		s_shuttingDown = true;
		s_pendingQueue.Clear();

		foreach (AchievementNotificationWindow window in s_activeNotifications.ToArray())
			window.Close();
	}

	/// <summary>
	/// Reposition every active notification so they stack upward from the bottom-right.
	/// </summary>
	private static void RepositionAll()
	{
		if (s_activeNotifications.Count == 0)
			return;

		AchievementNotificationWindow first = s_activeNotifications[0];
		Screen? screen = first.Screens.Primary ?? (first.Screens.All.Count > 0 ? first.Screens.All[0] : null);
		if (screen is null)
			return;

		PixelRect workArea = screen.WorkingArea;
		double scaling = screen.Scaling;

				// Walk from the newest notification, which sits at the bottom, up to the oldest
		double y = workArea.Y / scaling + workArea.Height / scaling - ScreenMargin;
		for (int i = s_activeNotifications.Count - 1; i >= 0; i--)
		{
			AchievementNotificationWindow win = s_activeNotifications[i];
			y -= win.Height;

			// Recalculated per window in case widths ever differ
			double x = workArea.X / scaling + workArea.Width / scaling - win.Width - ScreenMargin;
			win.Position = new PixelPoint((int)(x * scaling), (int)(y * scaling));

			y -= StackGap;
		}
	}

	private async Task AnimateLifecycleAsync()
	{
		await AnimateOpacityAsync(0, 1, FadeDuration);
		await Task.Delay(DisplayDuration);
		await AnimateOpacityAsync(1, 0, FadeDuration);
		Close();
	}

	private async Task AnimateOpacityAsync(
		double from,
		double to,
		TimeSpan duration)
	{
		int steps = (int)(duration / FadeStep);
		if (steps <= 0)
		{
			Dispatcher.UIThread.Post(() => Opacity = to);
			return;
		}

		double delta = (to - from) / steps;
		for (int i = 0; i <= steps; i++)
		{
			double value = from + delta * i;
			Dispatcher.UIThread.Post(() => Opacity = value);
			await Task.Delay(FadeStep);
		}

		Dispatcher.UIThread.Post(() => Opacity = to);
	}

	private async Task LoadIconAsync(string url)
	{
		try
		{
			byte[] bytes;

			if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile)
			{
				bytes = await File.ReadAllBytesAsync(uri.LocalPath);
			}
			else
			{
				bytes = await s_httpClient.GetByteArrayAsync(url);
			}

			using MemoryStream stream = new(bytes);
			Bitmap bitmap = new(stream);
			Dispatcher.UIThread.Post(() =>
			{
				// The window may have closed while the icon was downloading, and nothing else would ever
				// dispose this bitmap
				if (_closed)
				{
					bitmap.Dispose();
					return;
				}

				(AchievementIcon.Source as Bitmap)?.Dispose();
				AchievementIcon.Source = bitmap;
			});
		}
		catch
		{
			// Icon load failure is non-critical
		}
	}

	private void MakeTopmost()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return;

		try
		{
			nint handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
			if (handle != IntPtr.Zero)
			{
				const int SWP_NOMOVE = 0x0002;
				const int SWP_NOSIZE = 0x0001;
				const int SWP_NOACTIVATE = 0x0010;
				nint HWND_TOPMOST = new(-1);
				SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
			}
		}
		catch
		{
			// Non-critical; Avalonia Topmost already set as fallback.
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(
		IntPtr hWnd,
		IntPtr hWndInsertAfter,
		int x,
		int y,
		int cx,
		int cy,
		uint uFlags);

	private static void PlayNotificationSound()
	{
		if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return;

		try
		{
			s_notificationSoundData ??= LoadEmbeddedSound();
			if (s_notificationSoundData is null)
				return;

			const uint SND_MEMORY = 0x0004;
			const uint SND_ASYNC = 0x0001;
			const uint SND_NODEFAULT = 0x0002;
			PlaySound(s_notificationSoundData, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
		}
		catch { }
	}

	private static byte[]? LoadEmbeddedSound()
	{
		using Stream? stream = typeof(AchievementNotificationWindow).Assembly.GetManifestResourceStream("TinyTrophy.Assets.Notification.wav");
		if (stream is null)
			return null;

		byte[] buffer = new byte[stream.Length];
		stream.ReadExactly(buffer);

		return buffer;
	}

	[DllImport("winmm.dll")]
	private static extern bool PlaySound(
		byte[]? lpszName,
		IntPtr hmod,
		uint fdwSound);
}
