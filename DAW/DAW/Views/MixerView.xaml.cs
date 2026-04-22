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
    /// Converts IsCompactMode bool to channel width (compact=50, expanded=75).
    /// </summary>
    public class CompactWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? 50.0 : 75.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// The main mixer view displaying all track channel strips.
    /// </summary>
    public partial class MixerView : UserControl
    {
        // Default values for reset
        private const double DefaultMasterVolume = 0.8;
        private const double DefaultMasterPan = 0.0;

        public static readonly DependencyProperty IsCompactModeProperty =
            DependencyProperty.Register(nameof(IsCompactMode), typeof(bool), typeof(MixerView),
                new PropertyMetadata(false));

        public bool IsCompactMode
        {
            get => (bool)GetValue(IsCompactModeProperty);
            set => SetValue(IsCompactModeProperty, value);
        }

        public static readonly DependencyProperty ShowMasterFxProperty =
            DependencyProperty.Register(nameof(ShowMasterFx), typeof(bool), typeof(MixerView),
                new PropertyMetadata(false));

        public bool ShowMasterFx
        {
            get => (bool)GetValue(ShowMasterFxProperty);
            set => SetValue(ShowMasterFxProperty, value);
        }

        public MixerView()
        {
            InitializeComponent();

            Loaded += (s, e) =>
            {
                SubscribeToRoutingChanges();
                Dispatcher.InvokeAsync(RedrawRoutingCables, System.Windows.Threading.DispatcherPriority.Loaded);
            };

            DataContextChanged += (s, e) =>
            {
                SubscribeToRoutingChanges();
                Dispatcher.InvokeAsync(RedrawRoutingCables, System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }
        
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        // ── View Switching (Mixer / Patchbay) ──────────────────────────────
        
        public static readonly DependencyProperty ShowPatchbayProperty =
            DependencyProperty.Register(nameof(ShowPatchbay), typeof(bool), typeof(MixerView),
                new PropertyMetadata(false));

        public bool ShowPatchbay
        {
            get => (bool)GetValue(ShowPatchbayProperty);
            set => SetValue(ShowPatchbayProperty, value);
        }

        private void OnShowMixerView(object sender, MouseButtonEventArgs e)
        {
            ShowPatchbay = false;
            if (ViewModel != null)
                ViewModel.StatusMessage = "🎚️ Mixer-Ansicht";
        }

        private void OnShowPatchbayView(object sender, MouseButtonEventArgs e)
        {
            ShowPatchbay = true;
            if (ViewModel != null)
                ViewModel.StatusMessage = "🔌 Patchbay-Ansicht";
        }

        private void ToggleCompactMode(object sender, RoutedEventArgs e)
        {
            IsCompactMode = !IsCompactMode;
            if (ViewModel != null)
                ViewModel.StatusMessage = IsCompactMode ? "Compact-Ansicht" : "Erweiterte Ansicht";
        }

        private void OnShowChannelFx(object sender, MouseButtonEventArgs e) => ShowMasterFx = false;
        private void OnShowMasterFx(object sender, MouseButtonEventArgs e) => ShowMasterFx = true;

        private void OnAddFxToActivePanel(object sender, RoutedEventArgs e)
        {
            if (ShowMasterFx)
                OnMasterSlotAddFirst();
            else
                OnAddEffect(sender, e);
        }

        private void OnMasterSlotAddFirst()
        {
            if (ViewModel == null) return;
            var slot = ViewModel.MasterEffectSlots.FirstOrDefault(s => !s.HasEffect);
            if (slot == null) { ViewModel.StatusMessage = "✗ Keine freien Master-Slots"; return; }
            OpenMasterPluginSearch(slot.SlotNumber);
        }

        private void OnMasterSlotAdd(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                int? num = btn.Tag switch { int i => i, string s when int.TryParse(s, out var p) => p, _ => null };
                if (num.HasValue) OpenMasterPluginSearch(num.Value);
            }
        }

        private void OpenMasterPluginSearch(int slotNumber)
        {
            if (ViewModel == null) return;
            // Use the same CommandPalette but without a track (master bus)
            var palette = new Plugins.CommandPalette(null)
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (palette.ShowDialog() == true && palette.SelectedPlugin != null)
            {
                var slot = ViewModel.MasterEffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
                if (slot != null && !slot.HasEffect)
                {
                    try
                    {
                        var effect = palette.SelectedPlugin.Factory();
                        if (effect != null)
                        {
                            effect.IsExpanded = true;
                            slot.Effect = effect;
                            palette.SelectedPlugin.UsageCount++;
                            palette.SelectedPlugin.LastUsed = DateTime.Now;
                            ViewModel.StatusMessage = $"✓ {effect.Name} → Master Slot {slotNumber}";
                        }
                    }
                    catch (Exception ex)
                    {
                        ViewModel.StatusMessage = $"✗ Fehler: {ex.Message}";
                    }
                }
            }
        }

        private void OnMasterSlotRemove(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && ViewModel != null)
            {
                int? num = btn.Tag switch { int i => i, string s when int.TryParse(s, out var p) => p, _ => null };
                if (!num.HasValue) return;
                var slot = ViewModel.MasterEffectSlots.FirstOrDefault(s => s.SlotNumber == num.Value);
                if (slot?.Effect != null)
                {
                    var name = slot.DisplayName;
                    slot.Effect = null;
                    slot.IsExpanded = false;
                    ViewModel.StatusMessage = $"✓ {name} aus Master Slot {num.Value} entfernt";
                }
            }
        }

        private void OnMasterSlotOpen(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || ViewModel == null) return;
            int? num = el.Tag switch { int i => i, string s when int.TryParse(s, out var p) => p, _ => null };
            if (!num.HasValue) return;
            var slot = ViewModel.MasterEffectSlots.FirstOrDefault(s => s.SlotNumber == num.Value);
            if (slot?.Effect is not { } effect) return;

            var pluginDef = Plugins.PluginManager.Instance.Plugins.FirstOrDefault(p => p.Name == effect.Name)
                ?? new Plugins.PluginDefinition
                {
                    Id = effect.EffectType, Name = effect.Name, Category = "Effect",
                    Icon = effect.Icon, Description = effect.Name, Factory = () => effect
                };
            Plugins.PluginWindow.Show(effect, pluginDef, null, Window.GetWindow(this));
        }
        
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
                if (_isDragging) return;
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

                Plugins.PluginWindow.Show(effect, pluginDef, track, Window.GetWindow(this));
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

        #endregion

        // ── Horizontal Scrolling with Mousewheel ────────────────────────────

        private void MixerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // Scroll horizontally instead of vertically
                double offset = scrollViewer.HorizontalOffset - (e.Delta / 3.0);
                scrollViewer.ScrollToHorizontalOffset(offset);
                e.Handled = true;
            }
        }

        // ── Effect Reordering with Mousewheel ──────────────────────────────

        private void EffectSlot_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is FrameworkElement element && 
                element.Tag is int slotNumber &&
                ViewModel?.SelectedTrack?.EffectChain is { } chain)
            {
                var slot = ViewModel.SelectedTrack.EffectSlots.FirstOrDefault(s => s.SlotNumber == slotNumber);
                if (slot?.Effect == null) return;

                var effects = chain.Effects.ToList();
                int currentIndex = effects.IndexOf(slot.Effect);
                if (currentIndex < 0) return;

                int newIndex;
                if (e.Delta > 0) // Scroll up = move up (toward slot 1)
                {
                    newIndex = currentIndex == 0 ? effects.Count - 1 : currentIndex - 1;
                }
                else // Scroll down = move down (toward last slot)
                {
                    newIndex = currentIndex == effects.Count - 1 ? 0 : currentIndex + 1;
                }

                // Reorder
                effects.RemoveAt(currentIndex);
                effects.Insert(newIndex, slot.Effect);

                // Rebuild effect chain
                chain.Effects.Clear();
                foreach (var fx in effects)
                    chain.AddEffect(fx);

                ViewModel.StatusMessage = $"↕ {slot.Effect.Name} → Slot {newIndex + 1}";
                e.Handled = true;
            }
        }

        // ── FL Studio-Style Mixer Routing ──────────────────────────────────

        /// <summary>
        /// Adds a new empty mixer channel.
        /// </summary>
        private void AddEmptyChannel_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.AddEmptyMixerChannel();
        }

        /// <summary>
        /// Selects a mixer channel for routing.
        /// </summary>
        private void MixerChannel_Click(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _reorderDragFromTitle = IsOriginatingFromTitle(e.OriginalSource);

            if (sender is Border { Tag: Models.Mixer.MixerChannel channel })
            {
                if (ViewModel != null)
                {
                    ViewModel.SelectedMixerChannel = channel;
                    ViewModel.StatusMessage = $"Channel {channel.ChannelNumber} ausgewählt – Ziehe OUT→IN zum Routen, Titel zum Verschieben";
                }
                e.Handled = true;
            }
        }

        private static bool IsOriginatingFromTitle(object? originalSource)
        {
            var el = originalSource as System.Windows.Media.Visual;
            while (el != null)
            {
                if (el is FrameworkElement fe && fe.Tag as string == "CHANNEL_TITLE")
                    return true;
                el = System.Windows.Media.VisualTreeHelper.GetParent(el) as System.Windows.Media.Visual;
            }
            return false;
        }

        private void RoutingTarget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Models.Mixer.MixerChannel targetChannel } &&
                ViewModel?.SelectedMixerChannel is { } sourceChannel)
            {
                ViewModel.ToggleChannelRouting(sourceChannel, targetChannel);
                e.Handled = true;
            }
        }

        // ── Channel Reorder Drag & Drop ────────────────────────────────────

        private const string ChannelReorderFormat = "DAW_ChannelReorder";
        private bool _isChannelReorderDrag;
        private bool _reorderDragFromTitle;

        private void MixerChannel_HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (_isRoutingDrag || _isChannelReorderDrag) return;
            if (!_reorderDragFromTitle) return; // only drag when initiated from title
            if (sender is not Border { Tag: Models.Mixer.MixerChannel channel } border) return;

            var diff = e.GetPosition(null) - _dragStartPoint;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _isChannelReorderDrag = true;
            DragDrop.DoDragDrop(border, new DataObject(ChannelReorderFormat, channel), DragDropEffects.Move);
            _isChannelReorderDrag = false;
        }

        /// <summary>Handles drop of an effect slot OR a channel-reorder onto a mixer channel strip.</summary>
        private void MixerChannel_Drop(object sender, DragEventArgs e)
        {
            // ── Channel reorder ──────────────────────────────────────────
            if (e.Data.GetDataPresent(ChannelReorderFormat))
            {
                if (e.Data.GetData(ChannelReorderFormat) is not Models.Mixer.MixerChannel draggedChannel) return;

                // Find the target MixerChannel from the drop target's Tag
                Models.Mixer.MixerChannel? targetChannel = null;
                var el = sender as FrameworkElement;
                while (el != null)
                {
                    if (el.Tag is Models.Mixer.MixerChannel mc) { targetChannel = mc; break; }
                    el = System.Windows.Media.VisualTreeHelper.GetParent(el) as FrameworkElement;
                }

                if (targetChannel != null && targetChannel != draggedChannel && ViewModel != null)
                {
                    int targetIndex = ViewModel.MixerChannels.IndexOf(targetChannel);
                    ViewModel.MoveMixerChannel(draggedChannel, targetIndex);
                }

                e.Handled = true;
                return;
            }

            // ── Effect slot copy ─────────────────────────────────────────
            if (!e.Data.GetDataPresent(EffectSlotDragFormat)) return;
            if (e.Data.GetData(EffectSlotDragFormat) is not EffectSlot sourceSlot) return;
            if (sourceSlot.Effect is null) return;

            Track? targetTrack = null;
            if (sender is FrameworkElement fe)
            {
                var current = fe;
                while (current != null)
                {
                    if (current.DataContext is Track t) { targetTrack = t; break; }
                    current = current.Parent as FrameworkElement ??
                              System.Windows.Media.VisualTreeHelper.GetParent(current) as FrameworkElement;
                }
            }

            if (targetTrack is null) return;

            var emptySlot = targetTrack.EffectSlots.FirstOrDefault(s => !s.HasEffect);
            if (emptySlot is null)
            {
                if (ViewModel != null) ViewModel.StatusMessage = "✗ Kein freier Slot im Ziel-Track";
                return;
            }

            var clonedEffect = sourceSlot.Effect.Clone();
            if (clonedEffect is null)
            {
                if (ViewModel != null) ViewModel.StatusMessage = "✗ Effekt konnte nicht kopiert werden";
                return;
            }

            emptySlot.Effect = clonedEffect;
            if (ViewModel != null)
                ViewModel.StatusMessage = $"✓ {clonedEffect.Name} nach {targetTrack.Title} Slot {emptySlot.SlotNumber} kopiert";

            e.Handled = true;
        }

        private void MixerChannel_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = (e.Data.GetDataPresent(EffectSlotDragFormat) || e.Data.GetDataPresent(ChannelReorderFormat))
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        // ── Routing Cable Drag & Draw ───────────────────────────────────────

        private bool _isRoutingDrag;
        private Models.Mixer.MixerChannel? _routingSourceChannel;
        private Point _routingDragStartOnOverlay;
        private System.Windows.Shapes.Path? _routingDragPath;

        private void SubscribeToRoutingChanges()
        {
            if (ViewModel?.MixerChannels is not System.Collections.ObjectModel.ObservableCollection<Models.Mixer.MixerChannel> channels)
                return;

            channels.CollectionChanged -= OnRoutingCollectionChanged;
            channels.CollectionChanged += OnRoutingCollectionChanged;

            foreach (var ch in channels)
            {
                ch.SendTargets.CollectionChanged -= OnRoutingSendTargetsChanged;
                ch.SendTargets.CollectionChanged += OnRoutingSendTargetsChanged;
            }
        }

        private void OnRoutingCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            SubscribeToRoutingChanges();
            Dispatcher.InvokeAsync(RedrawRoutingCables, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void OnRoutingSendTargetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(RedrawRoutingCables, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void MixerScrollViewer_RoutingMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Walk up from the original source to find an element tagged IO_SOCKET
            var outSocket = FindTaggedAncestorOrSelf(e.OriginalSource as DependencyObject, "IO_SOCKET");
            if (outSocket == null) return;

            var channel = FindMixerChannelFromElement(outSocket);
            if (channel == null) return;

            var overlay = FindName("RoutingOverlay") as Canvas;
            if (overlay == null) return;

            _isRoutingDrag = true;
            _routingSourceChannel = channel;

            try
            {
                _routingDragStartOnOverlay = outSocket.TranslatePoint(
                    new Point(outSocket.ActualWidth / 2, outSocket.ActualHeight / 2), overlay);
            }
            catch
            {
                _isRoutingDrag = false;
                return;
            }

            _routingDragPath = CreateRoutingPath(_routingDragStartOnOverlay, _routingDragStartOnOverlay, channel.Color, true);
            overlay.Children.Add(_routingDragPath);

            ((ScrollViewer)sender).CaptureMouse();
        }

        private void MixerScrollViewer_RoutingMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isRoutingDrag || _routingDragPath == null) return;

            var overlay = FindName("RoutingOverlay") as Canvas;
            if (overlay == null) return;

            UpdateRoutingPath(_routingDragPath, _routingDragStartOnOverlay, e.GetPosition(overlay));
        }

        private void MixerScrollViewer_RoutingMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isRoutingDrag) return;

            var scrollViewer = (ScrollViewer)sender;
            var overlay = FindName("RoutingOverlay") as Canvas;

            if (overlay != null && _routingDragPath != null)
                overlay.Children.Remove(_routingDragPath);
            _routingDragPath = null;

            scrollViewer.ReleaseMouseCapture();

            if (_routingSourceChannel != null)
            {
                // Hit-test relative to the ScrollViewer (captures the channel IN sockets)
                // and also check the MixerView root for the master IN socket.
                var dropOnScroll = e.GetPosition(scrollViewer);
                var inSocket = HitTestForSocketTag(scrollViewer, dropOnScroll, "IO_SOCKET");

                // Also check master IN socket (outside ScrollViewer)
                if (inSocket == null)
                {
                    var dropOnView = e.GetPosition(this);
                    inSocket = HitTestForSocketTag(this, dropOnView, "MASTER_IN_SOCKET");
                }

                if (inSocket != null)
                {
                    if (inSocket.Tag as string == "MASTER_IN_SOCKET")
                    {
                        _routingSourceChannel.SendTargets.Clear();
                        RedrawRoutingCables();
                        if (ViewModel != null)
                            ViewModel.StatusMessage = $"🔌 {_routingSourceChannel.Name} → MASTER";
                    }
                    else
                    {
                        var targetChannel = FindMixerChannelFromElement(inSocket);
                        if (targetChannel != null && targetChannel != _routingSourceChannel)
                        {
                            if (_routingSourceChannel.SendTargets.Contains(targetChannel.ChannelNumber))
                            {
                                // Removing an existing send — always allowed
                                _routingSourceChannel.RemoveSend(targetChannel.ChannelNumber);
                                RedrawRoutingCables();
                                if (ViewModel != null)
                                    ViewModel.StatusMessage = $"🔌 {_routingSourceChannel.Name} ⊗ {targetChannel.Name}";
                            }
                            else
                            {
                                // Adding a new send — check for cycles first
                                if (ViewModel != null && ViewModel.WouldCreateCycle(_routingSourceChannel, targetChannel))
                                {
                                    ViewModel.StatusMessage = $"✗ Loop verhindert: {targetChannel.Name} → … → {_routingSourceChannel.Name} existiert bereits";
                                }
                                else
                                {
                                    _routingSourceChannel.AddSend(targetChannel.ChannelNumber);
                                    RedrawRoutingCables();
                                    if (ViewModel != null)
                                        ViewModel.StatusMessage = $"🔌 {_routingSourceChannel.Name} → {targetChannel.Name}";
                                }
                            }
                        }
                    }
                }
            }

            _isRoutingDrag = false;
            _routingSourceChannel = null;
        }

        /// <summary>
        /// Redraws all routing cables on the RoutingOverlay canvas (inside the ScrollViewer).
        /// Cables are naturally clipped to the visible mixer area.
        /// </summary>
        private void RedrawRoutingCables()
        {
            var overlay = FindName("RoutingOverlay") as Canvas;
            if (overlay == null || ViewModel == null) return;

            var toRemove = overlay.Children.OfType<System.Windows.Shapes.Path>()
                .Where(p => p != _routingDragPath).ToList();
            foreach (var p in toRemove)
                overlay.Children.Remove(p);

            var channelList = ViewModel.MixerChannels.ToList();

            foreach (var sourceChannel in channelList)
            {
                foreach (var targetNum in sourceChannel.SendTargets)
                {
                    var targetChannel = channelList.FirstOrDefault(c => c.ChannelNumber == targetNum);
                    if (targetChannel == null) continue;

                    var outSocket = FindSocketInScrollViewer(sourceChannel, "IO_SOCKET");
                    var inSocket  = FindSocketInScrollViewer(targetChannel,  "IO_SOCKET");
                    if (outSocket == null || inSocket == null) continue;

                    try
                    {
                        var start = outSocket.TranslatePoint(
                            new Point(outSocket.ActualWidth / 2, outSocket.ActualHeight / 2), overlay);
                        var end = inSocket.TranslatePoint(
                            new Point(inSocket.ActualWidth / 2, inSocket.ActualHeight / 2), overlay);

                        var cable = CreateRoutingPath(start, end, sourceChannel.Color, false);
                        cable.ToolTip = $"{sourceChannel.Name} → {targetChannel.Name}\nKlick zum Trennen";

                        var src = sourceChannel;
                        var tgt = targetChannel;
                        cable.MouseLeftButtonDown += (s, ev) =>
                        {
                            src.RemoveSend(tgt.ChannelNumber);
                            RedrawRoutingCables();
                            if (ViewModel != null)
                                ViewModel.StatusMessage = $"🔌 {src.Name} ✗ {tgt.Name}";
                            ev.Handled = true;
                        };

                        overlay.Children.Add(cable);
                    }
                    catch { /* element not yet in visual tree */ }
                }
            }
        }

        private FrameworkElement? FindSocketInScrollViewer(Models.Mixer.MixerChannel channel, string socketTag)
        {
            var scrollViewer = FindName("MixerScrollViewer") as DependencyObject;
            if (scrollViewer == null) return null;
            return FindSocketRecursive(scrollViewer, channel, socketTag);
        }

        private FrameworkElement? FindSocketRecursive(DependencyObject parent, Models.Mixer.MixerChannel channel, string socketTag)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is MixerChannelControl channelControl &&
                    channelControl.DataContext is Track track &&
                    ViewModel?.MixerChannels.FirstOrDefault(mc => mc.SourceTrack == track) == channel)
                {
                    return FindTaggedElement(channelControl, socketTag);
                }

                var result = FindSocketRecursive(child, channel, socketTag);
                if (result != null) return result;
            }
            return null;
        }

        private static FrameworkElement? FindTaggedElement(DependencyObject parent, string tag)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement fe && fe.Tag as string == tag) return fe;
                var result = FindTaggedElement(child, tag);
                if (result != null) return result;
            }
            return null;
        }

        private Models.Mixer.MixerChannel? FindMixerChannelFromElement(FrameworkElement element)
        {
            DependencyObject? current = element;
            while (current != null)
            {
                if (current is MixerChannelControl ctrl && ctrl.DataContext is Track t && ViewModel != null)
                    return ViewModel.MixerChannels.FirstOrDefault(mc => mc.SourceTrack == t);
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static FrameworkElement? HitTestForSocketTag(UIElement root, Point position, string tag)
        {
            FrameworkElement? found = null;
            System.Windows.Media.VisualTreeHelper.HitTest(
                root, null,
                result =>
                {
                    var current = result.VisualHit as DependencyObject;
                    while (current != null)
                    {
                        if (current is FrameworkElement fe && fe.Tag as string == tag)
                        {
                            found = fe;
                            return System.Windows.Media.HitTestResultBehavior.Stop;
                        }
                        current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                    }
                    return System.Windows.Media.HitTestResultBehavior.Continue;
                },
                new System.Windows.Media.PointHitTestParameters(position));
            return found;
        }

        /// <summary>Walks up the visual tree from <paramref name="element"/> (inclusive) to find
        /// the first <see cref="FrameworkElement"/> whose <c>Tag</c> equals <paramref name="tag"/>.</summary>
        private static FrameworkElement? FindTaggedAncestorOrSelf(DependencyObject? element, string tag)
        {
            var current = element;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.Tag as string == tag)
                    return fe;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static System.Windows.Shapes.Path CreateRoutingPath(
            Point start, Point end,
            System.Windows.Media.Color color,
            bool isDragging)
        {
            var geometry = new System.Windows.Media.PathGeometry();
            var figure   = new System.Windows.Media.PathFigure { StartPoint = start };
            double offset = Math.Max(40, Math.Abs(end.Y - start.Y) * 0.6);
            figure.Segments.Add(new System.Windows.Media.BezierSegment(
                new Point(start.X, start.Y + offset),
                new Point(end.X,   end.Y   - offset),
                end, true));
            geometry.Figures.Add(figure);

            var brushColor = isDragging
                ? System.Windows.Media.Color.FromArgb(160, color.R, color.G, color.B)
                : color;

            var path = new System.Windows.Shapes.Path
            {
                Data            = geometry,
                Stroke          = new System.Windows.Media.SolidColorBrush(brushColor),
                StrokeThickness = isDragging ? 2 : 3,
                StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
                StrokeEndLineCap   = System.Windows.Media.PenLineCap.Round,
                IsHitTestVisible   = !isDragging,
                Cursor = isDragging ? Cursors.Arrow : Cursors.Hand
            };

            if (!isDragging)
            {
                path.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = color,
                    BlurRadius  = 8,
                    ShadowDepth = 0,
                    Opacity     = 0.65
                };
            }

            return path;
        }

        private static void UpdateRoutingPath(System.Windows.Shapes.Path path, Point start, Point end)
        {
            var geometry = new System.Windows.Media.PathGeometry();
            var figure   = new System.Windows.Media.PathFigure { StartPoint = start };
            double offset = Math.Max(40, Math.Abs(end.Y - start.Y) * 0.6);
            figure.Segments.Add(new System.Windows.Media.BezierSegment(
                new Point(start.X, start.Y + offset),
                new Point(end.X,   end.Y   - offset),
                end, true));
            geometry.Figures.Add(figure);
            path.Data = geometry;
        }
    }
}