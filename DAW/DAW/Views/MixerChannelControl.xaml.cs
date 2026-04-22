using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DAW.Models;
using DAW.ViewModels;

namespace DAW.Views;

/// <summary>
/// Multiplies a double value by a fraction parameter. Used to compute proportional heights.
/// </summary>
public class FractionConverter : IValueConverter
{
    public static readonly FractionConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var frac))
            return Math.Max(0, d * frac);
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// A mixer channel strip control representing a single track's mixer controls.
/// </summary>
public partial class MixerChannelControl : UserControl
{
    // Default values for reset
    private const double DefaultVolume = 0.8;
    private const double DefaultPan = 0.0;
    
    public MixerChannelControl()
    {
        InitializeComponent();
    }
    
    /// <summary>
    /// Dependency property to track if this channel is selected
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(MixerChannelControl), 
            new PropertyMetadata(false));
    
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(MixerChannelControl),
            new PropertyMetadata(false));

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
    
    #region Channel Selection
    
    /// <summary>
    /// Selects this channel when clicked
    /// </summary>
    private void Channel_Select(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is Track track)
        {
            // Find the MainViewModel through the visual tree
            var mainViewModel = FindMainViewModel();
            if (mainViewModel != null)
            {
                mainViewModel.SelectedTrack = track;
            }
        }
    }
    
    /// <summary>
    /// Finds the MainViewModel from the parent hierarchy
    /// </summary>
    private MainViewModel? FindMainViewModel()
    {
        // Walk up the visual tree to find the MixerView and get its DataContext
        DependencyObject? parent = this;
        while (parent != null)
        {
            if (parent is MixerView mixerView && mixerView.DataContext is MainViewModel vm)
            {
                return vm;
            }
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }
    
    #endregion
    
    #region Fader Reset Handlers
    
    /// <summary>
    /// Resets volume to default (0.8) on right-click.
    /// </summary>
    private void Volume_ResetToDefault(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is Track track)
        {
            track.Volume = DefaultVolume;
            e.Handled = true;
        }
    }
    
    /// <summary>
    /// Resets pan to center (0.0) on right-click.
    /// </summary>
    private void Pan_ResetToDefault(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is Track track)
        {
            track.Pan = DefaultPan;
            e.Handled = true;
        }
    }

    /// <summary>
    /// Right-click on the OUT socket: removes this channel from the master bus
    /// by clearing all SendTargets, so it no longer routes to any channel.
    /// </summary>
    private void OutSocket_RightClick(object sender, MouseButtonEventArgs e)
    {
        var mainVm = FindMainViewModel();
        if (mainVm == null) return;

        if (DataContext is not Track track) return;

        var mixerChannel = mainVm.MixerChannels.FirstOrDefault(mc => mc.SourceTrack == track);
        if (mixerChannel == null) return;

        if (mixerChannel.SendTargets.Count == 0)
        {
            mainVm.StatusMessage = $"ℹ {mixerChannel.Name} ist bereits direkt am Master";
            e.Handled = true;
            return;
        }

        mixerChannel.SendTargets.Clear();
        mainVm.StatusMessage = $"🔌 {mixerChannel.Name} → Master (alle Sends entfernt)";
        e.Handled = true;
    }

    /// <summary>
    /// Right-click on solo: if multiple tracks are soloed, unsolo all.
    /// </summary>
    private void Solo_RightClick(object sender, MouseButtonEventArgs e)
    {
        var mainVm = FindMainViewModel();
        if (mainVm == null) return;

        var soloedTracks = mainVm.Tracks.Where(t => t.IsSolo).ToList();
        if (soloedTracks.Count > 1)
        {
            foreach (var track in soloedTracks)
                track.IsSolo = false;
            mainVm.StatusMessage = "✓ Alle Tracks unsolo";
            e.Handled = true;
        }
    }
    
    #endregion
}

