using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Models.Sequencer;

/// <summary>
/// Represents a single step in the step sequencer.
/// Each step can be active or inactive, and carries per-step modulation values.
/// </summary>
public class StepModel : INotifyPropertyChanged
{
    private bool   _isActive;
    private float  _velocity = 1.0f;   // 0.0 – 1.0
    private float  _pan      = 0.0f;   // -1 (L) – +1 (R)
    private float  _pitch    = 0.0f;   // semitones offset

    /// <summary>Whether this step triggers a note.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    /// <summary>Per-step velocity multiplier (0–1). Applied on top of the channel default.</summary>
    public float Velocity
    {
        get => _velocity;
        set => SetField(ref _velocity, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Per-step pan offset (-1 … +1).</summary>
    public float Pan
    {
        get => _pan;
        set => SetField(ref _pan, Math.Clamp(value, -1f, 1f));
    }

    /// <summary>Per-step pitch offset in semitones (-24 … +24).</summary>
    public float Pitch
    {
        get => _pitch;
        set => SetField(ref _pitch, Math.Clamp(value, -24f, 24f));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
