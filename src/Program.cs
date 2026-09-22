using Avalonia;
using TinyTrophy.Infrastructure;

namespace TinyTrophy;

class Program
{
	// Rooted in a static field so the GC cannot finalize it while the app runs: the finalizer would
	// close the handle, release ownership of the name and let a second instance start.
	private static Mutex? s_singleInstanceMutex;

	// Nothing before BuildAvaloniaApp may use Avalonia or third-party APIs — the framework isn't ready yet.
	[STAThread]
	public static void Main(string[] args)
	{
		s_singleInstanceMutex = new Mutex(true, "TinyTrophy_SingleInstance", out bool createdNew);
		if (!createdNew)
			return;

		// Embedded native libraries must be on disk and loaded before Avalonia P/Invokes into them.
		NativeLibraryLoader.Initialize();

		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

		GC.KeepAlive(s_singleInstanceMutex);
	}

	// Required by the Avalonia visual designer.
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
#if DEBUG
		.WithDeveloperTools()
#endif
		.LogToTrace();
}
