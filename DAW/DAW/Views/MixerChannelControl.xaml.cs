using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DAW.Models;
using DAW.ViewModels;

namespace DAW.Views;

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
    
    #endregion
}
