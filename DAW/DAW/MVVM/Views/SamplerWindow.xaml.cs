using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DAW.MVVM.Models;
using DAW.MVVM.ViewModels;

namespace DAW.MVVM.Views;

/// <summary>
/// Sampler/Clip Editor window for detailed audio editing.
/// Opens with a Track as DataContext following MVVM pattern.
/// </summary>
public partial class SamplerWindow : Window
{
    private readonly SamplerViewModel _viewModel;
    
    public SamplerWindow(Track track)
    {
        InitializeComponent();
        
        _viewModel = new SamplerViewModel(track);
        DataContext = _viewModel;
        
        // Cleanup on close
        Closed += (_, _) => _viewModel.Dispose();
    }
    
    /// <summary>
    /// Opens the sampler window for a specific track.
    /// </summary>
    public static void ShowForTrack(Track track, Window? owner = null)
    {
        var window = new SamplerWindow(track)
        {
            Owner = owner
        };
        window.Show();
    }
}

/// <summary>
/// Converts boolean to "ON"/"OFF" string for toggle buttons.
/// </summary>
public class BoolToOnOffConverter : IValueConverter
{
    public static readonly BoolToOnOffConverter Instance = new();
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "🟢 ON" : "🔴 OFF";
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString()?.Contains("ON") == true;
    }
}

/// <summary>
/// Converts sample position to normalized 0-1 range for waveform editor.
/// Requires ClipData.TotalSamples via MultiBinding or parameter.
/// </summary>
public class SamplesToNormalizedConverter : IValueConverter
{
    public static readonly SamplesToNormalizedConverter Instance = new();
    
    // Store total samples for conversion (set from binding context)
    public static long TotalSamples { get; set; } = 1;
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long samples && TotalSamples > 0)
        {
            return (double)samples / TotalSamples;
        }
        return 0.0;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double normalized && TotalSamples > 0)
        {
            return (long)(normalized * TotalSamples);
        }
        return 0L;
    }
}
