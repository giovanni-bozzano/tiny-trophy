using Avalonia;

namespace TinyTrophy;

class Program
{

	// Nothing before BuildAvaloniaApp may use Avalonia or third-party APIs — the framework isn't ready yet.
	[STAThread]
	public static void Main(string[] args)
	{
		_ = new Mutex(true, "TinyTrophy_SingleInstance", out bool createdNew);
		if (!createdNew)
			return;

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
