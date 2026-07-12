using System.Globalization;
using System.Windows.Data;
using DAW.MVVM.ViewModels.PianoRoll;

namespace DAW.Converters;

/// <summary>
/// Converts an enum value to a bool by comparing it to a string parameter.
/// Used for ToggleButton.IsChecked binding in the Piano Roll toolbar.
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.ToString() == parameter.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
            return Enum.Parse(targetType, parameter.ToString()!);
        return Binding.DoNothing;
    }
}

/// <summary>
/// Converts the total pitch count to a pixel height for the note canvas.
/// Height = TotalPitches * DefaultCellH * ZoomY
/// Since ZoomY is not available here we use a fixed default.
/// </summary>
public class PitchHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return (double)(count * PianoRollViewModel.DefaultCellH);
        return 1792.0;  // 128 * 14
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
