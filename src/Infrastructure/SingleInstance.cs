namespace TinyTrophy.Infrastructure;

/// <summary>
/// Guards against a second copy of the app running at the same time.
/// </summary>
public static class SingleInstance
{
	private const string MutexName = "TinyTrophy_SingleInstance";

	// Rooted in a static field so the GC cannot finalize it while the app runs: the finalizer would
	// close the handle, destroy the named object and let a second instance start.
	private static Mutex? s_mutex;

	/// <summary>
	/// Attempts to claim the single-instance lock. Returns <see langword="false"/> when another
	/// instance already holds it, in which case the caller should exit.
	/// </summary>
	public static bool TryAcquire()
	{
		s_mutex = new Mutex(true, MutexName, out bool createdNew);
		if (createdNew)
			return true;

		Release();
		return false;
	}

	/// <summary>
	/// Closes the handle, destroying the named object so a replacement process can claim the lock.
	/// </summary>
	/// <remarks>
	/// Required before relaunching during an update: the updater starts the new executable before this
	/// process exits, and <c>createdNew</c> is false for as long as the named object exists — releasing
	/// ownership is not enough, the handle has to be closed. Otherwise the new version would start,
	/// observe the lock and silently exit, leaving the user with no running app.
	/// </remarks>
	public static void Release()
	{
		s_mutex?.Dispose();
		s_mutex = null;
	}
}
