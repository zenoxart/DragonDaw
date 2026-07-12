using System.ComponentModel;
using System.Runtime.CompilerServices;
using DAW.Audio;
using DAW.Audio.Effects;

namespace DAW.MVVM.Models;

/// <summary>
/// Represents a single effect slot in the master effects rack.
/// When an effect is set/cleared, it is automatically added/removed from the master EffectChain.
/// </summary>
public class MasterEffectSlot : INotifyPropertyChanged
{
    private AudioEffect? _effect;
    private bool _isExpanded;
    private int _slotNumber;
    private EffectChain? _ownerChain;

    public MasterEffectSlot(int slotNumber)
    {
        _slotNumber = slotNumber;
    }

    /// <summary>
    /// Links this slot to the master EffectChain so changes propagate to the audio pipeline.
    /// </summary>
    internal void SetOwnerChain(EffectChain chain) => _ownerChain = chain;

    public int SlotNumber
    {
        get => _slotNumber;
        set => SetField(ref _slotNumber, value);
    }

    public AudioEffect? Effect
    {
        get => _effect;
        set
        {
            if (_effect == value) return;

            // Remove old effect from audio chain
            if (_effect != null)
                _ownerChain?.RemoveEffect(_effect);

            _effect = value;

            // Add new effect to audio chain
            if (_effect != null)
                _ownerChain?.AddEffect(_effect);

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEffect));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Icon));
        }
    }

    public bool HasEffect => _effect != null;
    public string DisplayName => _effect?.Name ?? $"Slot {_slotNumber}";
    public string Icon => _effect?.Icon ?? "➕";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
