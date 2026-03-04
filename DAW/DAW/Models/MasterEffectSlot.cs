using System.ComponentModel;
using System.Runtime.CompilerServices;
using DAW.Audio.Effects;

namespace DAW.Models;

/// <summary>
/// Represents a single effect slot in the master effects rack.
/// </summary>
public class MasterEffectSlot : INotifyPropertyChanged
{
    private AudioEffect? _effect;
    private bool _isExpanded;
    private int _slotNumber;

    public MasterEffectSlot(int slotNumber)
    {
        _slotNumber = slotNumber;
    }

    /// <summary>
    /// Slot number (1-10).
    /// </summary>
    public int SlotNumber
    {
        get => _slotNumber;
        set => SetField(ref _slotNumber, value);
    }

    /// <summary>
    /// The effect in this slot (null if empty).
    /// </summary>
    public AudioEffect? Effect
    {
        get => _effect;
        set
        {
            if (SetField(ref _effect, value))
            {
                OnPropertyChanged(nameof(HasEffect));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    /// <summary>
    /// Whether this slot has an effect.
    /// </summary>
    public bool HasEffect => _effect != null;

    /// <summary>
    /// Display name for the slot.
    /// </summary>
    public string DisplayName => _effect?.Name ?? $"Slot {_slotNumber}";

    /// <summary>
    /// Icon for the slot.
    /// </summary>
    public string Icon => _effect?.Icon ?? "➕";

    /// <summary>
    /// Whether the effect panel is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
