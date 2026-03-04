using System.ComponentModel;
using System.Runtime.CompilerServices;
using DAW.Audio.Effects;

namespace DAW.Models;

/// <summary>
/// Represents a single effect slot in a track's effect chain.
/// Each track has 10 fixed slots.
/// </summary>
public class EffectSlot : INotifyPropertyChanged
{
    private AudioEffect? _effect;
    
    public EffectSlot(int slotNumber)
    {
        SlotNumber = slotNumber;
    }
    
    /// <summary>
    /// The slot number (1-10)
    /// </summary>
    public int SlotNumber { get; }
    
    /// <summary>
    /// The effect in this slot (null if empty)
    /// </summary>
    public AudioEffect? Effect
    {
        get => _effect;
        set
        {
            if (_effect != value)
            {
                _effect = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasEffect));
            }
        }
    }
    
    /// <summary>
    /// Whether this slot has an effect loaded
    /// </summary>
    public bool HasEffect => _effect != null;
    
    /// <summary>
    /// Clears the effect from this slot
    /// </summary>
    public void Clear()
    {
        Effect = null;
    }

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
