using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DAW.Audio.Effects;

namespace DAW.Audio;

/// <summary>
/// Thread-safe effect chain for audio processing.
/// Uses copy-on-write pattern to avoid locks during audio processing.
/// </summary>
public class EffectChain : INotifyPropertyChanged
{
    private volatile bool _isBypassed;
    private volatile AudioEffect[] _effectsSnapshot = [];
    private readonly object _modifyLock = new();

    public EffectChain()
    {
        Effects.CollectionChanged += (_, _) =>
        {
            UpdateSnapshot();
            OnPropertyChanged(nameof(EffectCount));
        };
    }

    /// <summary>
    /// The ordered list of effects in this chain (for UI binding).
    /// </summary>
    public ObservableCollection<AudioEffect> Effects { get; } = [];

    /// <summary>
    /// Thread-safe snapshot of effects for audio processing.
    /// </summary>
    public AudioEffect[] EffectsSnapshot => _effectsSnapshot;

    /// <summary>
    /// Bypass the entire effect chain.
    /// </summary>
    public bool IsBypassed
    {
        get => _isBypassed;
        set
        {
            if (_isBypassed != value)
            {
                _isBypassed = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Number of effects in the chain.
    /// </summary>
    public int EffectCount => _effectsSnapshot.Length;

    private void UpdateSnapshot()
    {
        lock (_modifyLock)
        {
            _effectsSnapshot = [.. Effects];
        }
    }

    /// <summary>
    /// Adds an effect to the end of the chain.
    /// </summary>
    public void AddEffect(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            Effects.Add(effect);
        }
    }

    /// <summary>
    /// Inserts an effect at a specific position.
    /// </summary>
    public void InsertEffect(int index, AudioEffect effect)
    {
        lock (_modifyLock)
        {
            Effects.Insert(Math.Clamp(index, 0, Effects.Count), effect);
        }
    }

    /// <summary>
    /// Removes an effect from the chain.
    /// </summary>
    public bool RemoveEffect(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            return Effects.Remove(effect);
        }
    }

    /// <summary>
    /// Moves an effect up in the chain (earlier in processing order).
    /// </summary>
    public void MoveEffectUp(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            int index = Effects.IndexOf(effect);
            if (index > 0)
            {
                Effects.Move(index, index - 1);
            }
        }
    }

    /// <summary>
    /// Moves an effect down in the chain (later in processing order).
    /// </summary>
    public void MoveEffectDown(AudioEffect effect)
    {
        lock (_modifyLock)
        {
            int index = Effects.IndexOf(effect);
            if (index >= 0 && index < Effects.Count - 1)
            {
                Effects.Move(index, index + 1);
            }
        }
    }

    /// <summary>
    /// Resets all effects in the chain.
    /// </summary>
    public void Reset()
    {
        var snapshot = _effectsSnapshot;
        foreach (var effect in snapshot)
        {
            try { effect.Reset(); } catch { }
        }
    }

    /// <summary>
    /// Clears all effects from the chain.
    /// </summary>
    public void Clear()
    {
        lock (_modifyLock)
        {
            Effects.Clear();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
