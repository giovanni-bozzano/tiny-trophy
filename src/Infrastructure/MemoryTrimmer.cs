using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TinyTrophy.Infrastructure;

/// <summary>
/// Forces freed managed memory to actually be handed back to the OS.
/// </summary>
/// <remarks>
/// A GC pass alone only frees objects inside the process's managed heap segments; it does not shrink the
/// process's working set, so Task Manager keeps reporting the high-water mark. Calling
/// <c>SetProcessWorkingSetSize</c> with -1/-1 asks Windows to trim the working set down to what is
/// actually resident right now, which is the standard trick apps use to shrink their footprint after a
/// large, one-off release of memory (e.g. hiding to the tray).
/// </remarks>
public static partial class MemoryTrimmer
{
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	[SupportedOSPlatform("windows")]
	private static partial bool SetProcessWorkingSetSize(
		IntPtr process,
		nint minimumWorkingSetSize,
		nint maximumWorkingSetSize);

	/// <summary>
	/// Collects garbage and, on Windows, trims the process working set so freed memory is released back
	/// to the OS instead of being held onto as free space within the process.
	/// </summary>
	public static void CollectAndTrim()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			try
			{
				SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
			}
			catch
			{
				// Best effort — not critical if trimming fails
			}
		}
	}
}
