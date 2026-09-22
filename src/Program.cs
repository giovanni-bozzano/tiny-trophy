using Avalonia;
using TinyTrophy.Infrastructure;

namespace TinyTrophy;

class Program
{
	// Nothing before BuildAvaloniaApp may use Avalonia or third-party APIs — the framework isn't ready yet.
	[STAThread]
	public static void Main(string[] args)
	{
		if (!SingleInstance.TryAcquire())
			return;

		// Embedded native libraries must be on disk and loaded before Avalonia P/Invokes into them.
		NativeLibraryLoader.Initialize();

		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	// Required by the Avalonia visual designer.
	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
		.UsePlatformDetect()
#if DEBUG
		.WithDeveloperTools()
#endif
		.LogToTrace();
}
