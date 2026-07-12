using DAW.MVVM.Views.Mixer;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace DAW.MVVM.Views.Mixer;

/// <summary>
/// Concrete implementation of the Mixer Effect Rack View.
/// Implements IMixerEffectRackView and handles all UI interactions.
/// 
/// This View:
/// - NEVER directly accesses any Model
/// - Only exposes events for the Presenter to handle
/// - Only accepts data through interface methods
/// </summary>
public partial class MixerEffectRackView : UserControl, IMixerEffectRackView
{
    private readonly ObservableCollection<EffectSlotViewData> _slots = [];
    private bool _suppressMixEvents;

    public MixerEffectRackView()
    {
        InitializeComponent();
        SlotsContainer.ItemsSource = _slots;
    }

    #region IMixerEffectRackView Events

    public event EventHandler<int>? SlotExpandToggled;
    public event EventHandler<SlotBypassChangedEventArgs>? SlotBypassChanged;
    public event EventHandler<SlotMixChangedEventArgs>? SlotMixChanged;
    public event EventHandler<int>? SlotAddEffectRequested;
    public event EventHandler<int>? SlotRemoveEffectRequested;
    public event EventHandler<int>? SlotOpenEditorRequested;

    #endregion

    #region IMixerEffectRackView Methods

    public void SetTitle(string title)
    {
        TitleText.Text = title;
    }

    public void SetSlots(IReadOnlyList<EffectSlotViewData> slots)
    {
        _suppressMixEvents = true;
        try
        {
            _slots.Clear();
            foreach (var slot in slots)
            {
                _slots.Add(slot);
            }
        }
        finally
        {
            _suppressMixEvents = false;
        }
    }

    public void UpdateSlot(int slotIndex, EffectSlotViewData slotData)
    {
        _suppressMixEvents = true;
        try
        {
            var index = slotIndex - 1; // Convert to 0-based
            if (index >= 0 && index < _slots.Count)
            {
                _slots[index] = slotData;
            }
        }
        finally
        {
            _suppressMixEvents = false;
        }
    }

    public void ShowLoading(bool isLoading)
    {
        LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowStatus(string message)
    {
        StatusText.Text = message;
        StatusBar.Visibility = Visibility.Visible;
        
        // Auto-hide after 3 seconds
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (_, _) =>
        {
            StatusBar.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    #endregion

    #region UI Event Handlers

    private void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int slotIndex)
        {
            SlotExpandToggled?.Invoke(this, slotIndex);
        }
    }

    private void OnSlotClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int slotIndex)
        {
            var slot = _slots.FirstOrDefault(s => s.SlotIndex == slotIndex);
            if (slot == null) return;
            
            if (slot.HasPlugin)
            {
                // Open editor for existing plugin
                SlotOpenEditorRequested?.Invoke(this, slotIndex);
            }
            else
            {
                // Add new effect to empty slot
                SlotAddEffectRequested?.Invoke(this, slotIndex);
            }
        }
    }

    private void OnBypassClicked(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && toggle.Tag is int slotIndex)
        {
            var isActive = toggle.IsChecked ?? false;
            SlotBypassChanged?.Invoke(this, new SlotBypassChangedEventArgs(slotIndex, isActive));
        }
    }

    private void OnMixChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressMixEvents) return;
        
        if (sender is Slider slider && slider.Tag is int slotIndex)
        {
            SlotMixChanged?.Invoke(this, new SlotMixChangedEventArgs(slotIndex, (float)e.NewValue));
        }
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int slotIndex)
        {
            SlotRemoveEffectRequested?.Invoke(this, slotIndex);
        }
    }

    #endregion
}

/// <summary>
/// Converter for expand/collapse icon
/// </summary>
public class ExpandIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isExpanded && isExpanded ? "▼" : "▶";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
