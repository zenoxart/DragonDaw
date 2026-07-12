using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Models.PianoRoll;

/// <summary>
/// A single MIDI note in the Piano Roll.
/// The Piano Roll stores data in ticks (PPQ-based).
/// </summary>
public class PianoRollNote : INotifyPropertyChanged
{
    private int   _pitch      = 60;   // MIDI note 0–127 (60 = C5)
    private int   _startTick  = 0;
    private int   _length     = 96;   // in ticks  (96 ticks = 1 beat at PPQ=96)
    private float _velocity   = 0.8f; // 0–1
    private float _pan        = 0.0f; // -1..+1
    private float _release    = 0.5f; // 0..1
    private int   _colorIndex = 0;
    private bool  _isSelected = false;
    private bool  _isMuted    = false;

    // ── Note data ─────────────────────────────────────────────────────────────

    /// <summary>MIDI pitch 0–127. C5 = 60.</summary>
    public int Pitch
    {
        get => _pitch;
        set => SetField(ref _pitch, Math.Clamp(value, 0, 127));
    }

    /// <summary>Note start position in ticks from pattern start.</summary>
    public int StartTick
    {
        get => _startTick;
        set => SetField(ref _startTick, Math.Max(0, value));
    }

    /// <summary>Note duration in ticks. Minimum = 1 tick.</summary>
    public int Length
    {
        get => _length;
        set => SetField(ref _length, Math.Max(1, value));
    }

    public int EndTick => StartTick + Length;

    /// <summary>Velocity 0–1.</summary>
    public float Velocity
    {
        get => _velocity;
        set => SetField(ref _velocity, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Pan -1 (L) … +1 (R).</summary>
    public float Pan
    {
        get => _pan;
        set => SetField(ref _pan, Math.Clamp(value, -1f, 1f));
    }

    /// <summary>Release time 0–1 (relative to note length).</summary>
    public float Release
    {
        get => _release;
        set => SetField(ref _release, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Color index for grouping / chord marking.</summary>
    public int ColorIndex
    {
        get => _colorIndex;
        set => SetField(ref _colorIndex, value);
    }

    // ── Edit state ────────────────────────────────────────────────────────────

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    // ── Display helpers ───────────────────────────────────────────────────────

    private static readonly string[] NoteNames = ["C","C#","D","D#","E","F","F#","G","G#","A","A#","B"];

    /// <summary>Returns note name, e.g. "C5", "A#3".</summary>
    public string NoteName => $"{NoteNames[_pitch % 12]}{_pitch / 12 - 1}";

    public bool IsBlackKey => new[] { 1, 3, 6, 8, 10 }.Contains(_pitch % 12);

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
