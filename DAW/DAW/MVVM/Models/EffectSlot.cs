using System.ComponentModel;
using System.Runtime.CompilerServices;
using DAW.Audio;
using DAW.Audio.Effects;

namespace DAW.MVVM.Models;

/// <summary>
/// Represents a single effect slot in a track's effect chain.
/// Each track has 10 fixed slots.
/// When an effect is added or removed, the owning track's EffectChain is updated automatically.
/// </summary>
public class EffectSlot : INotifyPropertyChanged
{
    private AudioEffect? _effect;
    private EffectChain? _ownerChain;
    
    public EffectSlot(int slotNumber)
    {
        SlotNumber = slotNumber;
    }
    
    /// <summary>
    /// Links this slot to the track's EffectChain so changes propagate to the audio pipeline.
    /// </summary>
    internal void SetOwnerChain(EffectChain chain) => _ownerChain = chain;
    
    /// <summary>
    /// The slot number (1-10)
    /// </summary>
    public int SlotNumber { get; }
    
    /// <summary>
    /// The effect in this slot (null if empty).
    /// Setting this automatically adds/removes the effect from the audio EffectChain.
    /// </summary>
    public AudioEffect? Effect
    {
        get => _effect;
        set
        {
            if (_effect == value) return;
            
            // Remove old effect from the audio chain
            if (_effect != null)
                _ownerChain?.RemoveEffect(_effect);
            
            _effect = value;
            
            // Add new effect to the audio chain
            if (_effect != null)
                _ownerChain?.AddEffect(_effect);
            
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEffect));
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
