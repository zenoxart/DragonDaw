using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.Models.Mixer;

/// <summary>
/// Model for a single effect slot in a mixer channel.
/// Contains the effect configuration and parameters.
/// </summary>
public sealed class EffectSlotModel : INotifyPropertyChanged
{
    private bool _isActive = true;
    private string? _pluginId;
    private string _pluginName = string.Empty;
    private string _pluginIcon = "🎛️";
    private float _slotMix = 1.0f;
    private bool _isExpanded;

    /// <summary>
    /// Slot index (1-10)
    /// </summary>
    public int SlotIndex { get; init; }

    /// <summary>
    /// Whether the effect is active (not bypassed)
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    /// <summary>
    /// Unique plugin identifier (null if slot is empty)
    /// </summary>
    public string? PluginId
    {
        get => _pluginId;
        set
        {
            if (SetField(ref _pluginId, value))
            {
                OnPropertyChanged(nameof(HasPlugin));
            }
        }
    }

    /// <summary>
    /// Display name of the plugin
    /// </summary>
    public string PluginName
    {
        get => _pluginName;
        set => SetField(ref _pluginName, value);
    }

    /// <summary>
    /// Icon for the plugin
    /// </summary>
    public string PluginIcon
    {
        get => _pluginIcon;
        set => SetField(ref _pluginIcon, value);
    }

    /// <summary>
    /// Whether this slot has a plugin loaded
    /// </summary>
    public bool HasPlugin => !string.IsNullOrEmpty(PluginId);

    /// <summary>
    /// Dry/Wet mix amount (0.0 = fully dry, 1.0 = fully wet)
    /// </summary>
    public float SlotMix
    {
        get => _slotMix;
        set => SetField(ref _slotMix, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// Whether the slot UI is expanded to show parameters
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// Effect parameters stored by parameter name
    /// </summary>
    public Dictionary<string, float> Parameters { get; set; } = [];

    /// <summary>
    /// Gets a parameter value or returns the default
    /// </summary>
    public float GetParameter(string name, float defaultValue = 0.0f)
    {
        return Parameters.TryGetValue(name, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Sets a parameter value
    /// </summary>
    public void SetParameter(string name, float value)
    {
        Parameters[name] = value;
        OnPropertyChanged(nameof(Parameters));
    }

    /// <summary>
    /// Clears the slot (removes the plugin)
    /// </summary>
    public void Clear()
    {
        PluginId = null;
        PluginName = string.Empty;
        PluginIcon = "🎛️";
        Parameters.Clear();
        SlotMix = 1.0f;
        IsActive = true;
        IsExpanded = false;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
