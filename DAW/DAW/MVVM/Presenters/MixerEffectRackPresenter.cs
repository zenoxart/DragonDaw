using DAW.MVVM.Models.Mixer;
using DAW.MVVM.Views.Mixer;
using DAW.Services;

namespace DAW.Presenters;

/// <summary>
/// Presenter for the Mixer Effect Rack.
/// Acts as the sole bridge between the View and the Model.
/// 
/// Responsibilities:
/// - Reacts to Model changes (SelectedChannelIndex)
/// - Transforms Model data into View data
/// - Handles View events
/// - Updates the Model
/// - Notifies the AudioEngine
/// 
/// Flow:
/// View <-- Events --> Presenter <-- Data --> Model
///                          |
///                          v
///                    AudioEngine
/// </summary>
public sealed class MixerEffectRackPresenter : IDisposable
{
    private readonly IMixerEffectRackView _view;
    private readonly MixerModel _model;
    private readonly IAudioEngineService _audioEngine;
    
    private MixerChannelModel? _currentChannel;
    private bool _isDisposed;

    public MixerEffectRackPresenter(
        IMixerEffectRackView view,
        MixerModel model,
        IAudioEngineService audioEngine)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        
        // Subscribe to View events
        SubscribeToViewEvents();
        
        // Subscribe to Model events
        SubscribeToModelEvents();
        
        // Initial load
        OnChannelChanged(_model.SelectedChannelIndex);
    }

    #region View Event Subscriptions

    private void SubscribeToViewEvents()
    {
        _view.SlotExpandToggled += OnSlotExpandToggled;
        _view.SlotBypassChanged += OnSlotBypassChanged;
        _view.SlotMixChanged += OnSlotMixChanged;
        _view.SlotAddEffectRequested += OnSlotAddEffectRequested;
        _view.SlotRemoveEffectRequested += OnSlotRemoveEffectRequested;
        _view.SlotOpenEditorRequested += OnSlotOpenEditorRequested;
    }

    private void UnsubscribeFromViewEvents()
    {
        _view.SlotExpandToggled -= OnSlotExpandToggled;
        _view.SlotBypassChanged -= OnSlotBypassChanged;
        _view.SlotMixChanged -= OnSlotMixChanged;
        _view.SlotAddEffectRequested -= OnSlotAddEffectRequested;
        _view.SlotRemoveEffectRequested -= OnSlotRemoveEffectRequested;
        _view.SlotOpenEditorRequested -= OnSlotOpenEditorRequested;
    }

    #endregion

    #region Model Event Subscriptions

    private void SubscribeToModelEvents()
    {
        _model.SelectedChannelChanged += OnSelectedChannelChanged;
    }

    private void UnsubscribeFromModelEvents()
    {
        _model.SelectedChannelChanged -= OnSelectedChannelChanged;
        
        if (_currentChannel != null)
        {
            _currentChannel.PropertyChanged -= OnChannelPropertyChanged;
            foreach (var slot in _currentChannel.EffectSlots)
            {
                slot.PropertyChanged -= OnSlotPropertyChanged;
            }
        }
    }

    #endregion

    #region Channel Selection

    /// <summary>
    /// Called when the selected channel changes in the Model
    /// </summary>
    private void OnSelectedChannelChanged(object? sender, int newIndex)
    {
        OnChannelChanged(newIndex);
    }

    /// <summary>
    /// Handles channel change - loads new channel data into the View
    /// </summary>
    private void OnChannelChanged(int channelIndex)
    {
        // Unsubscribe from old channel
        if (_currentChannel != null)
        {
            _currentChannel.PropertyChanged -= OnChannelPropertyChanged;
            foreach (var slot in _currentChannel.EffectSlots)
            {
                slot.PropertyChanged -= OnSlotPropertyChanged;
            }
        }
        
        // Get new channel
        _currentChannel = _model.SelectedChannel;
        
        if (_currentChannel == null)
        {
            _view.SetTitle("Mixer – No Channel");
            _view.SetSlots([]);
            return;
        }
        
        // Subscribe to new channel
        _currentChannel.PropertyChanged += OnChannelPropertyChanged;
        foreach (var slot in _currentChannel.EffectSlots)
        {
            slot.PropertyChanged += OnSlotPropertyChanged;
        }
        
        // Update View
        RefreshView();
    }

    /// <summary>
    /// Refreshes the entire View from current channel data
    /// </summary>
    private void RefreshView()
    {
        if (_currentChannel == null) return;
        
        // Set title
        var channelType = _currentChannel.ChannelType == MixerChannelType.Master ? "Master" : _currentChannel.Name;
        _view.SetTitle($"Mixer – {channelType}");
        
        // Convert Model slots to View data
        var viewData = _currentChannel.EffectSlots
            .Select(CreateSlotViewData)
            .ToList();
        
        _view.SetSlots(viewData);
    }

    /// <summary>
    /// Creates View data from a Model slot
    /// </summary>
    private static EffectSlotViewData CreateSlotViewData(EffectSlotModel slot)
    {
        return new EffectSlotViewData
        {
            SlotIndex = slot.SlotIndex,
            HasPlugin = slot.HasPlugin,
            PluginName = slot.PluginName,
            PluginIcon = slot.PluginIcon,
            IsActive = slot.IsActive,
            SlotMix = slot.SlotMix,
            IsExpanded = slot.IsExpanded
        };
    }

    #endregion

    #region View Event Handlers

    /// <summary>
    /// Handle: Slot expand/collapse toggled
    /// </summary>
    private void OnSlotExpandToggled(object? sender, int slotIndex)
    {
        var slot = _currentChannel?.GetSlot(slotIndex);
        if (slot == null) return;
        
        // Update Model
        slot.IsExpanded = !slot.IsExpanded;
        
        // View will update via PropertyChanged
    }

    /// <summary>
    /// Handle: Slot bypass state changed
    /// </summary>
    private void OnSlotBypassChanged(object? sender, SlotBypassChangedEventArgs e)
    {
        var slot = _currentChannel?.GetSlot(e.SlotIndex);
        if (slot == null || _currentChannel == null) return;
        
        // Update Model
        slot.IsActive = e.IsActive;
        
        // Notify AudioEngine
        _audioEngine.UpdateSlotBypass(_currentChannel.ChannelId, e.SlotIndex, e.IsActive);
        
        // Status feedback
        var status = e.IsActive ? "aktiviert" : "bypassed";
        _view.ShowStatus($"Slot {e.SlotIndex} {status}");
    }

    /// <summary>
    /// Handle: Slot mix value changed
    /// </summary>
    private void OnSlotMixChanged(object? sender, SlotMixChangedEventArgs e)
    {
        var slot = _currentChannel?.GetSlot(e.SlotIndex);
        if (slot == null || _currentChannel == null) return;
        
        // Update Model
        slot.SlotMix = e.MixValue;
        
        // Notify AudioEngine
        _audioEngine.UpdateSlotMix(_currentChannel.ChannelId, e.SlotIndex, e.MixValue);
    }

    /// <summary>
    /// Handle: Add effect to slot requested
    /// </summary>
    private void OnSlotAddEffectRequested(object? sender, int slotIndex)
    {
        // This would typically open a plugin browser/selector
        // For now, we just notify
        _view.ShowStatus($"Plugin-Browser für Slot {slotIndex} öffnen...");
        
        // The actual plugin loading would be done via:
        // LoadPluginToSlot(slotIndex, selectedPluginId);
    }

    /// <summary>
    /// Handle: Remove effect from slot requested
    /// </summary>
    private void OnSlotRemoveEffectRequested(object? sender, int slotIndex)
    {
        var slot = _currentChannel?.GetSlot(slotIndex);
        if (slot == null || !slot.HasPlugin || _currentChannel == null) return;
        
        var pluginName = slot.PluginName;
        
        // Notify AudioEngine first
        _audioEngine.UnloadPlugin(_currentChannel.ChannelId, slotIndex);
        
        // Update Model
        slot.Clear();
        
        // Update View
        _view.UpdateSlot(slotIndex, CreateSlotViewData(slot));
        _view.ShowStatus($"{pluginName} aus Slot {slotIndex} entfernt");
    }

    /// <summary>
    /// Handle: Open effect editor requested
    /// </summary>
    private void OnSlotOpenEditorRequested(object? sender, int slotIndex)
    {
        var slot = _currentChannel?.GetSlot(slotIndex);
        if (slot == null || !slot.HasPlugin) return;
        
        // This would typically open the plugin's native editor
        _view.ShowStatus($"Editor für {slot.PluginName} öffnen...");
    }

    #endregion

    #region Model Property Changed Handlers

    private void OnChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // If channel name changes, update title
        if (e.PropertyName == nameof(MixerChannelModel.Name))
        {
            RefreshView();
        }
    }

    private void OnSlotPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not EffectSlotModel slot) return;
        
        // Update the specific slot in the View
        _view.UpdateSlot(slot.SlotIndex, CreateSlotViewData(slot));
    }

    #endregion

    #region Public Methods for External Use

    /// <summary>
    /// Loads a plugin into a specific slot.
    /// Called from external sources (e.g., plugin browser).
    /// </summary>
    public void LoadPluginToSlot(int slotIndex, string pluginId, string pluginName, string pluginIcon = "🎛️")
    {
        var slot = _currentChannel?.GetSlot(slotIndex);
        if (slot == null || _currentChannel == null) return;
        
        // Unload existing plugin if any
        if (slot.HasPlugin)
        {
            _audioEngine.UnloadPlugin(_currentChannel.ChannelId, slotIndex);
        }
        
        // Update Model
        slot.PluginId = pluginId;
        slot.PluginName = pluginName;
        slot.PluginIcon = pluginIcon;
        slot.IsActive = true;
        slot.SlotMix = 1.0f;
        
        // Notify AudioEngine
        _audioEngine.LoadPlugin(_currentChannel.ChannelId, slotIndex, pluginId);
        
        _view.ShowStatus($"{pluginName} in Slot {slotIndex} geladen");
    }

    /// <summary>
    /// Forces a complete refresh of the View from the Model.
    /// Useful after loading a project from JSON.
    /// </summary>
    public void ForceRefresh()
    {
        OnChannelChanged(_model.SelectedChannelIndex);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_isDisposed) return;
        
        UnsubscribeFromViewEvents();
        UnsubscribeFromModelEvents();
        
        _isDisposed = true;
    }

    #endregion
}
