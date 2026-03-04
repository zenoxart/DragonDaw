namespace DAW.Views.Mixer;

/// <summary>
/// View data for displaying an effect slot.
/// This is a pure data transfer object - View never sees the Model directly.
/// </summary>
public sealed class EffectSlotViewData
{
    public int SlotIndex { get; init; }
    public bool HasPlugin { get; init; }
    public string PluginName { get; init; } = string.Empty;
    public string PluginIcon { get; init; } = "🎛️";
    public bool IsActive { get; init; } = true;
    public float SlotMix { get; init; } = 1.0f;
    public bool IsExpanded { get; init; }
    
    /// <summary>
    /// Display text for the slot (plugin name or "Empty")
    /// </summary>
    public string DisplayText => HasPlugin ? PluginName : "Empty";
    
    /// <summary>
    /// Mix display as percentage
    /// </summary>
    public string MixDisplay => $"{(int)(SlotMix * 100)}%";
}

/// <summary>
/// Interface for the Mixer Effect Rack View.
/// The View only exposes UI events and update methods - no direct Model access.
/// </summary>
public interface IMixerEffectRackView
{
    #region Events (View -> Presenter)

    /// <summary>
    /// Fired when a slot's expand button is clicked
    /// </summary>
    event EventHandler<int>? SlotExpandToggled;
    
    /// <summary>
    /// Fired when a slot's bypass/active state is toggled
    /// </summary>
    event EventHandler<SlotBypassChangedEventArgs>? SlotBypassChanged;
    
    /// <summary>
    /// Fired when a slot's mix knob value changes
    /// </summary>
    event EventHandler<SlotMixChangedEventArgs>? SlotMixChanged;
    
    /// <summary>
    /// Fired when user wants to add an effect to a slot
    /// </summary>
    event EventHandler<int>? SlotAddEffectRequested;
    
    /// <summary>
    /// Fired when user wants to remove an effect from a slot
    /// </summary>
    event EventHandler<int>? SlotRemoveEffectRequested;
    
    /// <summary>
    /// Fired when user wants to open effect parameters/editor
    /// </summary>
    event EventHandler<int>? SlotOpenEditorRequested;

    #endregion

    #region Methods (Presenter -> View)

    /// <summary>
    /// Sets the title of the effect rack (e.g., "Mixer – Master")
    /// </summary>
    void SetTitle(string title);
    
    /// <summary>
    /// Updates all effect slots with new data
    /// </summary>
    void SetSlots(IReadOnlyList<EffectSlotViewData> slots);
    
    /// <summary>
    /// Updates a single slot
    /// </summary>
    void UpdateSlot(int slotIndex, EffectSlotViewData slotData);
    
    /// <summary>
    /// Shows a loading indicator
    /// </summary>
    void ShowLoading(bool isLoading);
    
    /// <summary>
    /// Shows a status message
    /// </summary>
    void ShowStatus(string message);

    #endregion
}

#region Event Args

public sealed class SlotBypassChangedEventArgs : EventArgs
{
    public int SlotIndex { get; }
    public bool IsActive { get; }
    
    public SlotBypassChangedEventArgs(int slotIndex, bool isActive)
    {
        SlotIndex = slotIndex;
        IsActive = isActive;
    }
}

public sealed class SlotMixChangedEventArgs : EventArgs
{
    public int SlotIndex { get; }
    public float MixValue { get; }
    
    public SlotMixChangedEventArgs(int slotIndex, float mixValue)
    {
        SlotIndex = slotIndex;
        MixValue = mixValue;
    }
}

#endregion
