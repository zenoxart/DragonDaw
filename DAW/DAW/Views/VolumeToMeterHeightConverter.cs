using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DAW.Views;

/// <summary>
/// Shared dB ↔ linear helpers used by all mixer converters.
/// Range: −60 dB (silence floor) to +6 dB headroom.
/// </summary>
internal static class DbScale
{
    public const double MinDb = -60.0;
    public const double MaxDb =   6.0;
    public const double RangeDb = MaxDb - MinDb; // 66 dB

    /// <summary>Linear amplitude → dB (clamped to MinDb..MaxDb).</summary>
    public static double LinearToDb(double linear)
    {
        if (linear <= 0.0) return MinDb;
        var db = 20.0 * Math.Log10(linear);
        return Math.Clamp(db, MinDb, MaxDb);
    }

    /// <summary>dB → linear amplitude.</summary>
    public static double DbToLinear(double db)
    {
        if (db <= MinDb) return 0.0;
        return Math.Pow(10.0, db / 20.0);
    }

    /// <summary>Normalized 0..1 position on a dB-linear scale.</summary>
    public static double LinearToNorm(double linear)
        => Math.Clamp((LinearToDb(linear) - MinDb) / RangeDb, 0.0, 1.0);
}

/// <summary>
/// Two-way converter between linear amplitude (Volume property, 0–~2.0)
/// and a dB-linear fader position (slider value −60 to +6).
/// </summary>
public sealed class VolumeToDbFaderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double linear)
            return DbScale.LinearToDb(linear);
        return DbScale.MinDb;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double db)
            return DbScale.DbToLinear(db);
        return 0.0;
    }
}

/// <summary>
/// Converts linear amplitude (0–~2.0) to a pixel height on a dB-scaled meter.
/// Set <see cref="MaxHeight"/> to match the meter container's fixed height.
/// </summary>
public sealed class VolumeToMeterHeightConverter : IValueConverter
{
    public double MaxHeight { get; set; } = 140.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double volume)
            return DbScale.LinearToNorm(volume) * MaxHeight;
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// MultiValueConverter: values[0] = linear amplitude, values[1] = container ActualHeight.
/// Returns a pixel height scaled to the actual container.
/// Use with a MultiBinding on meter bar Height.
/// </summary>
public sealed class VolumeToMeterProportionalConverter : IMultiValueConverter
{
    public static readonly VolumeToMeterProportionalConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is double volume
            && values[1] is double containerHeight
            && containerHeight > 0)
        {
            return DbScale.LinearToNorm(volume) * containerHeight;
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// MultiValueConverter: values[0] = container ActualHeight, parameter = dB threshold (e.g. "0" or "-6").
/// Returns a Thickness with Top set to position the marker proportionally from the top.
/// 0 dB is at (1 - norm(1.0)) * height from top, -6 dB at (1 - norm(0.5)) * height, etc.
/// </summary>
public sealed class DbThresholdMarginConverter : IMultiValueConverter
{
    public static readonly DbThresholdMarginConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 1 && values[0] is double containerHeight && containerHeight > 0
            && parameter is string dbStr && double.TryParse(dbStr, CultureInfo.InvariantCulture, out var db))
        {
            // norm: 0 = bottom (-60dB), 1 = top (+6dB)
            double norm = Math.Clamp((db - DbScale.MinDb) / DbScale.RangeDb, 0.0, 1.0);
            double top = (1.0 - norm) * containerHeight;
            return new Thickness(0, top, 0, 0);
        }
        return new Thickness(0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts linear amplitude to a dB-based color:
///   green  below  –6 dB
///   orange from –6 dB to 0 dB
///   red    at/above 0 dB (headroom zone)
/// </summary>
public sealed class VolumeToMeterColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush  = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x40)));
    private static readonly SolidColorBrush OrangeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)));
    private static readonly SolidColorBrush RedBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x41, 0x36)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    private const double Threshold6dB = 0.5012;
    private const double Threshold0dB = 1.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double volume)
        {
            if (volume <= Threshold6dB) return GreenBrush;
            if (volume < Threshold0dB)  return OrangeBrush;
            return RedBrush;
        }
        return GreenBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
