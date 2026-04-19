using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using DAW.Audio.Effects;
using DAW.Models;
using DAW.ViewModels;

namespace DAW.Views
{
    /// <summary>
    /// Markup extension that returns all values of an enum type.
    /// Usage: {local:EnumValues {x:Type effects:EqBandMode}}
    /// </summary>
    public class EnumValuesExtension : MarkupExtension
    {
        public Type EnumType { get; }

        public EnumValuesExtension(Type enumType)
        {
            EnumType = enumType ?? throw new ArgumentNullException(nameof(enumType));
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Enum.GetValues(EnumType);
        }
    }

    /// <summary>
    /// Converter that shows Visible if object is not null, Collapsed otherwise.
    /// </summary>
    public class ObjectToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that shows Visible if object is null, Collapsed otherwise.
    /// </summary>
    public class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that shows Collapsed if object is null, Visible otherwise.
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that shows Visible if value is zero, Collapsed otherwise.
    /// </summary>
    public class ZeroToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue)
                return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is double doubleValue)
                return Math.Abs(doubleValue) < 0.001 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for track selection state.
    /// </summary>
    public class TrackSelectionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is Track track && values[1] is Track selectedTrack)
            {
                return track == selectedTrack;
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for expand/collapse button content.
    /// </summary>
    public class ExpandCollapseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded)
                return isExpanded ? "▼" : "▶";
            return "▶";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Inverse boolean to visibility converter.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// The main mixer view displaying all track channel strips.
    /// </summary>
    public partial class MixerView : UserControl
    {
        // Default values for reset
        private const double DefaultMasterVolume = 0.8;
        private const double DefaultMasterPan = 0.0;
        
        public MixerView()
        {
            InitializeComponent();
        }
        
        private MainViewModel? ViewModel => DataContext as MainViewModel;
        
        #region Fader Reset Handlers
        
        private void MasterVolume_ResetToDefault(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.MasterVolume = DefaultMasterVolume;
                ViewModel.StatusMessage = "Master Volume zurückgesetzt";
                e.Handled = true;
            }
        }
        
        private void MasterPan_ResetToDefault(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.MasterPan = DefaultMasterPan;
                ViewModel.StatusMessage = "Master Pan zurückgesetzt";
                e.Handled = true;
            }
        }
        
        #endregion
    
        private void OnAddEffect(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OnAddEffect called");
                
                if (ViewModel?.SelectedTrack == null)
                {
                    System.Diagnostics.Debug.WriteLine("No track selected");
                    if (ViewModel != null)
                        ViewModel.StatusMessage = "⚠ Bitte zuerst einen Track auswählen";
                    return;
                }
                
                // Restore plugin search functionality
                OpenPluginSearch();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnAddEffect: {ex}");
                if (ViewModel != null)
                    ViewModel.StatusMessage = $"✗ Fehler in OnAddEffect: {ex.Message}";
            }
        }
        
        #region Slot Event Handlers
        
        private void OnSlotAddEffect(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("OnSlotAddEffect called");
                
                if (sender is Button button)
                {
                    // Store the slot number for when plugin is selected
                    var slotNumber = button.Tag switch
                    {
                        int i => i,
                        string s when int.TryParse(s, out var parsed) => parsed,
                        _ => (int?)null
                    };
                    
                    System.Diagnostics.Debug.WriteLine($"Slot number: {slotNumber}");
                    
                    if (slotNumber.HasValue)
                    {
                        // Restore plugin search functionality
                        OpenPluginSearchForSlot(slotNumber.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnSlotAddEffect: {ex}");
                if (ViewModel != null)
                    ViewModel.StatusMessage = $"✗ Fehler in OnSlotAddEffect: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Adds a hardcoded test effect to isolate the problem.
        /// </summary>
        private void AddHardcodedTestEffect(int slotNumber)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Adding hardcoded test effect to slot {slotNumber}");
                
                if (ViewModel?.SelectedTrack == null)
                {
                    System.Diagnostics.Debug.WriteLine("ViewModel or SelectedTrack is null");
                    return;
                }
                
                if (ViewModel.SelectedTrack.EffectSlots == null)
                {
                    System.Diagnostics.Debug.WriteLine("EffectSlots collection is null");
                    return;
                }
                
                var slot = ViewModel.SelectedTrack.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
                System.Diagnostics.Debug.WriteLine($"Found slot: {slot != null}");
                
                if (slot != null && !slot.HasEffect)
                {
                    System.Diagnostics.Debug.WriteLine("Creating hardcoded EQ effect");
                    
                    // Create a simple EQ effect directly without factory
                    var effect = new DAW.Audio.Effects.EqualizerEffect();
                    System.Diagnostics.Debug.WriteLine($"Effect created: {effect.Name}");
                    
                    effect.IsExpanded = true;
                    System.Diagnostics.Debug.WriteLine("Effect expanded set to true");
                    
                    System.Diagnostics.Debug.WriteLine("Assigning effect to slot");
                    slot.Effect = effect;
                    System.Diagnostics.Debug.WriteLine($"Effect assigned. HasEffect: {slot.HasEffect}");
                    
                    if (ViewModel != null)
                        ViewModel.StatusMessage = $"✓ Test EQ in Slot {slotNumber} hinzugefügt";
                        
                    System.Diagnostics.Debug.WriteLine("Hardcoded effect successfully added");
                }
                else
                {
                    var message = slot == null ? "Slot nicht gefunden" : "Slot ist bereits belegt";
                    System.Diagnostics.Debug.WriteLine($"Cannot add effect: {message}");
                    
                    if (ViewModel != null)
                        ViewModel.StatusMessage = $"✗ {message}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in AddHardcodedTestEffect: {ex}");
                if (ViewModel != null)
                    ViewModel.StatusMessage = $"✗ Fehler beim Hinzufügen des Test-Effekts: {ex.Message}";
            }
        }
        
        private void OnSlotRemoveEffect(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && ViewModel?.SelectedTrack is { } track)
            {
                // Handle both int and string Tag values
                int? slotNumber = button.Tag switch
                {
                    int i => i,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    _ => null
                };
                
                if (slotNumber.HasValue)
                {
                    var slot = track.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber.Value);
                    if (slot?.Effect is { } effect)
                    {
                        // Save settings before removing
                        var pluginDef = Plugins.PluginManager.Instance.Plugins
                            .FirstOrDefault(p => p.Name == effect.Name);
                        if (pluginDef != null)
                        {
                            SaveEffectSettings(effect, pluginDef.Id, slotNumber.Value);
                        }
                        
                        var effectName = effect.Name;
                        slot.Effect = null;
                        if (ViewModel != null)
                            ViewModel.StatusMessage = $"✓ {effectName} aus Slot {slotNumber.Value} entfernt";
                    }
                }
            }
        }
        
        private void OnSlotToggleExpand(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && ViewModel?.SelectedTrack is { } track)
            {
                // Handle both int and string Tag values
                int? slotNumber = button.Tag switch
                {
                    int i => i,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    _ => null
                };
                
                if (!slotNumber.HasValue) return;
                
                var slot = track.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber.Value);
                if (slot?.Effect != null)
                {
                    slot.Effect.IsExpanded = !slot.Effect.IsExpanded;
                    
                    // Save the expanded state
                    var pluginDef = Plugins.PluginManager.Instance.Plugins
                        .FirstOrDefault(p => p.Name == slot.Effect.Name);
                    if (pluginDef != null)
                    {
                        SaveEffectSettings(slot.Effect, pluginDef.Id, slotNumber.Value);
                    }
                }
            }
        }
        
        /// <summary>
        /// Opens the plugin window for an existing effect in a slot.
        /// </summary>
        private void OnSlotOpenPlugin(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is not FrameworkElement element) return;
                if (ViewModel?.SelectedTrack is not { } track) return;
                
                int? slotNumber = element.Tag switch
                {
                    int i => i,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    _ => null
                };
                if (!slotNumber.HasValue) return;
                
                var slot = track.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber.Value);
                if (slot?.Effect is not { } effect) return;
                
                // Find the matching plugin definition for the window title/icon
                var pluginDef = Plugins.PluginManager.Instance.Plugins
                    .FirstOrDefault(p => p.Name == effect.Name)
                    ?? new Plugins.PluginDefinition
                    {
                        Id = effect.EffectType,
                        Name = effect.Name,
                        Category = "Effect",
                        Icon = effect.Icon,
                        Description = effect.Name,
                        Factory = () => effect
                    };
                
                var pluginWindow = new Plugins.PluginWindow(effect, pluginDef, track)
                {
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                pluginWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OnSlotOpenPlugin: {ex}");
                if (ViewModel != null)
                    ViewModel.StatusMessage = $"✗ Fehler beim Öffnen des Plugins: {ex.Message}";
            }
        }
        
        #endregion
        
        #region Plugin Search Methods
        
        /// <summary>
        /// Opens the plugin search for the first available slot.
        /// </summary>
        private void OpenPluginSearch()
        {
            if (ViewModel?.SelectedTrack == null) return;
            
            System.Diagnostics.Debug.WriteLine("Opening plugin search for first available slot");
            
            var commandPalette = new Plugins.CommandPalette(ViewModel.SelectedTrack)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            
            var result = commandPalette.ShowDialog();
            System.Diagnostics.Debug.WriteLine($"Plugin search result: {result}, Selected plugin: {commandPalette.SelectedPlugin?.Name}");
            
            if (result == true && commandPalette.SelectedPlugin != null)
            {
                // Find first empty slot
                var emptySlot = ViewModel.SelectedTrack.EffectSlots.FirstOrDefault(s => !s.HasEffect);
                if (emptySlot != null)
                {
                    AddEffectToSlot(commandPalette.SelectedPlugin, emptySlot.SlotNumber);
                }
                else
                {
                    if (ViewModel != null)
                        ViewModel.StatusMessage = "✗ Keine freien Slots verfügbar";
                }
            }
        }
        
        /// <summary>
        /// Opens the plugin search for a specific slot.
        /// </summary>
        private void OpenPluginSearchForSlot(int slotNumber)
        {
            if (ViewModel?.SelectedTrack == null) return;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"Opening plugin search for slot {slotNumber}");
                
                var commandPalette = new Plugins.CommandPalette(ViewModel.SelectedTrack)
                {
                    Owner = Window.GetWindow(this),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                
                System.Diagnostics.Debug.WriteLine("CommandPalette created");
                
                var result = commandPalette.ShowDialog();
                System.Diagnostics.Debug.WriteLine($"Plugin search result: {result}, Selected plugin: {commandPalette.SelectedPlugin?.Name ?? "null"}");
                
                if (result == true && commandPalette.SelectedPlugin != null)
                {
                    AddEffectToSlot(commandPalette.SelectedPlugin, slotNumber);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No plugin selected or dialog cancelled");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in OpenPluginSearchForSlot: {ex}");
                if (ViewModel != null)
                    ViewModel.StatusMessage = $"✗ Fehler beim Öffnen der Plugin-Suche: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Adds an effect from a plugin definition to a specific slot.
        /// </summary>
        private void AddEffectToSlot(Plugins.PluginDefinition plugin, int slotNumber)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Starting AddEffectToSlot: Plugin={plugin?.Name ?? "null"}, Slot={slotNumber}");
                
                if (ViewModel?.SelectedTrack == null)
                {
                    System.Diagnostics.Debug.WriteLine("ViewModel or SelectedTrack is null");
                    return;
                }
                
                if (ViewModel.SelectedTrack.EffectSlots == null)
                {
                    System.Diagnostics.Debug.WriteLine("EffectSlots collection is null");
                    return;
                }
                
                // Ensure we're on the UI thread
                if (!Dispatcher.CheckAccess())
                {
                    System.Diagnostics.Debug.WriteLine("Not on UI thread, invoking on UI thread");
                    Dispatcher.Invoke(() => AddEffectToSlot(plugin, slotNumber));
                    return;
                }
                
                var slot = ViewModel.SelectedTrack.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
                System.Diagnostics.Debug.WriteLine($"Found slot: {slot != null}, HasEffect: {slot?.HasEffect}");
                
                if (slot != null && !slot.HasEffect)
                {
                    System.Diagnostics.Debug.WriteLine("Creating effect from plugin factory");
                    AudioEffect effect;
                    
                    try
                    {
                        effect = plugin.Factory();
                        System.Diagnostics.Debug.WriteLine($"Effect created successfully: {effect?.Name ?? "null"}");
                        
                        if (effect == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Factory returned null effect");
                            if (ViewModel != null)
                                ViewModel.StatusMessage = $"✗ Factory für {plugin.Name} gab null zurück";
                            return;
                        }
                    }
                    catch (Exception factoryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Factory error: {factoryEx.Message}");
                        if (ViewModel != null)
                            ViewModel.StatusMessage = $"✗ Fehler beim Erstellen von {plugin.Name}: {factoryEx.Message}";
                        return;
                    }
                    
                    effect.IsExpanded = true;
                    System.Diagnostics.Debug.WriteLine("Effect expanded set to true");
                    
                    System.Diagnostics.Debug.WriteLine("Assigning effect to slot");
                    slot.Effect = effect;
                    System.Diagnostics.Debug.WriteLine($"Effect assigned. HasEffect: {slot.HasEffect}");
                    
                    // Update plugin usage statistics
                    plugin.UsageCount++;
                    plugin.LastUsed = DateTime.Now;
                    
                    if (ViewModel != null)
                        ViewModel.StatusMessage = $"✓ {plugin.Name} in Slot {slotNumber} hinzugefügt";
                        
                    System.Diagnostics.Debug.WriteLine($"Effect {plugin.Name} successfully added to slot {slotNumber}");
                }
                else
                {
                    var message = slot == null ? "✗ Slot nicht gefunden" : "✗ Slot ist bereits belegt";
                    if (ViewModel != null)
                        ViewModel.StatusMessage = message;
                        
                    System.Diagnostics.Debug.WriteLine($"Failed to add effect: {message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in AddEffectToSlot: {ex}");
                if (ViewModel != null)
                    ViewModel.StatusMessage = $"✗ Schwerwiegender Fehler beim Laden von {plugin?.Name ?? "unknown"}: {ex.Message}";
            }
        }
        
        #endregion
        
        #region Effect Settings Persistence
        
        private readonly Dictionary<string, Dictionary<string, object>> _effectSettings = new();
        
        /// <summary>
        /// Saves effect settings for later restoration.
        /// </summary>
        private void SaveEffectSettings(AudioEffect effect, string pluginId, int slotNumber)
        {
            var key = $"{pluginId}_{slotNumber}";
            var settings = new Dictionary<string, object>();
            
            // Save common properties
            settings["IsExpanded"] = effect.IsExpanded;
            settings["IsEnabled"] = effect.IsEnabled;
            
            // Save effect-specific properties using reflection
            var properties = effect.GetType().GetProperties()
                .Where(p => p.CanRead && p.CanWrite && 
                           p.PropertyType.IsValueType || p.PropertyType == typeof(string))
                .Where(p => p.Name != "IsExpanded" && p.Name != "IsEnabled"); // Already saved
            
            foreach (var prop in properties)
            {
                try
                {
                    settings[prop.Name] = prop.GetValue(effect) ?? "";
                }
                catch
                {
                    // Ignore properties that can't be read
                }
            }
            
            _effectSettings[key] = settings;
        }
        
        /// <summary>
        /// Restores effect settings from previous session.
        /// </summary>
        private void RestoreEffectSettings(AudioEffect effect, string pluginId, int slotNumber)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"RestoreEffectSettings called for {pluginId} in slot {slotNumber}");
                
                var key = $"{pluginId}_{slotNumber}";
                if (!_effectSettings.ContainsKey(key))
                {
                    System.Diagnostics.Debug.WriteLine("No saved settings found");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine("Saved settings found, but skipping restore for now");
                return; // Skip restore temporarily
                
                /*
                var settings = _effectSettings[key];
                
                foreach (var setting in settings)
                {
                    try
                    {
                        var property = effect.GetType().GetProperty(setting.Key);
                        if (property != null && property.CanWrite)
                        {
                            // Convert value to correct type
                            var convertedValue = Convert.ChangeType(setting.Value, property.PropertyType);
                            property.SetValue(effect, convertedValue);
                        }
                    }
                    catch (Exception propEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error restoring property {setting.Key}: {propEx.Message}");
                    }
                }
                */
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RestoreEffectSettings: {ex.Message}");
            }
        }
        
        #endregion
        
        private void OnRemoveEffectFromChain(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && 
                button.DataContext is AudioEffect effect &&
                ViewModel?.SelectedTrack?.EffectChain is { } chain)
            {
                chain.RemoveEffect(effect);
                ViewModel.StatusMessage = $"✓ {effect.Name} entfernt";
            }
        }

        #region Effect Slot Drag & Drop

        private Point _dragStartPoint;
        private bool _isDragging;
        private const string EffectSlotDragFormat = "DAW_EffectSlot";

        /// <summary>Begins drag from a filled effect slot.</summary>
        private void EffectSlot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void EffectSlot_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (sender is not FrameworkElement { DataContext: EffectSlot slot } element) return;
            if (slot.Effect is null) return;

            var pos = e.GetPosition(null);
            var diff = pos - _dragStartPoint;

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _isDragging = true;
            var data = new DataObject(EffectSlotDragFormat, slot);
            DragDrop.DoDragDrop(element, data, DragDropEffects.Copy);
            _isDragging = false;
        }

        /// <summary>Handles drop of an effect slot onto a mixer channel strip.</summary>
        private void MixerChannel_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(EffectSlotDragFormat)) return;
            if (e.Data.GetData(EffectSlotDragFormat) is not EffectSlot sourceSlot) return;
            if (sourceSlot.Effect is null) return;

            // Find the target track from the channel control's DataContext
            Track? targetTrack = null;
            if (sender is FrameworkElement fe)
            {
                var current = fe;
                while (current != null)
                {
                    if (current.DataContext is Track t)
                    {
                        targetTrack = t;
                        break;
                    }
                    current = current.Parent as FrameworkElement ?? 
                              System.Windows.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
                }
            }

            if (targetTrack is null) return;

            // Find first empty slot on target track
            var emptySlot = targetTrack.EffectSlots.FirstOrDefault(s => !s.HasEffect);
            if (emptySlot is null)
            {
                if (ViewModel != null)
                    ViewModel.StatusMessage = "✗ Kein freier Slot im Ziel-Track";
                return;
            }

            // Clone the effect with all settings
            var clonedEffect = sourceSlot.Effect.Clone();
            if (clonedEffect is null)
            {
                if (ViewModel != null)
                    ViewModel.StatusMessage = "✗ Effekt konnte nicht kopiert werden";
                return;
            }

            emptySlot.Effect = clonedEffect;

            if (ViewModel != null)
                ViewModel.StatusMessage = $"✓ {clonedEffect.Name} nach {targetTrack.Title} Slot {emptySlot.SlotNumber} kopiert";

            e.Handled = true;
        }

        private void MixerChannel_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(EffectSlotDragFormat)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        #endregion
    }
}