namespace TinyTrophy.Infrastructure;

/// <summary>
/// Converts Unix timestamps without throwing on corrupt save data.
/// </summary>
internal static class UnixTime
{
	// The range DateTimeOffset.FromUnixTimeSeconds accepts.
	private const long MinUnixSeconds = -62135596800;
	private const long MaxUnixSeconds = 253402300799;

	/// <summary>
	/// Converts <paramref name="unixSeconds"/> to local time, or returns <see langword="null"/> when the
	/// value is outside the representable range. A corrupt timestamp must not make an entire
	/// achievement file unreadable.
	/// </summary>
	public static DateTime? ToLocalDateTime(long unixSeconds)
	{
		if (unixSeconds < MinUnixSeconds || unixSeconds > MaxUnixSeconds)
			return null;

		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			// A local-time conversion at the very edge of the range can overflow; treat it as corrupt
			return null;
		}
	}
}
