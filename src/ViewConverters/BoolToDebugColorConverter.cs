using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace TinyTrophy.ViewConverters;

/// <summary>
/// Converts a bool (whether a debug candidate path exists on disk) to a foreground color, used in the
/// Settings directory debug panel.
/// </summary>
public sealed class BoolToDebugColorConverter
	: IValueConverter
{
	public static readonly BoolToDebugColorConverter Instance = new();

	private static readonly IBrush s_existsBrush = new SolidColorBrush(Color.Parse("#4caf50"));
	private static readonly IBrush s_missingBrush = new SolidColorBrush(Color.Parse("#e57373"));

	public object? Convert(
		object? value,
		Type targetType,
		object? parameter,
		CultureInfo culture)
	{
		return value is true ? s_existsBrush : s_missingBrush;
	}

	public object? ConvertBack(
		object? value,
		Type targetType,
		object? parameter,
		CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
