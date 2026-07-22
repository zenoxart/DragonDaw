using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// Represents a single band of the 7-band parametric EQ.
/// Filter state and coefficients are managed by <see cref="EqualizerEffect"/>
/// in flat arrays for cache-friendly access.
/// </summary>
public sealed class EqBand : INotifyPropertyChanged
{
    private double _gain;
    private double _frequency;
    private double _q = 1.0;
    private EqBandMode _mode = EqBandMode.Peaking;
    private bool _isEnabled = true;

    public EqBand(int number, double defaultFrequency, EqBandMode defaultMode = EqBandMode.Peaking)
    {
        Number    = number;
        _frequency = defaultFrequency;
        _mode      = defaultMode;
    }

    public int Number { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
    }

    public double Gain
    {
        get => _gain;
        set { var v = Math.Clamp(value, -18, 18); if (_gain != v) { _gain = v; OnPropertyChanged(); } }
    }

    public double Frequency
    {
        get => _frequency;
        set { var v = Math.Clamp(value, 20, 20000); if (_frequency != v) { _frequency = v; OnPropertyChanged(); } }
    }

    public double Q
    {
        get => _q;
        set { var v = Math.Clamp(value, 0.05, 30); if (_q != v) { _q = v; OnPropertyChanged(); } }
    }

    public EqBandMode Mode
    {
        get => _mode;
        set { if (_mode != value) { _mode = value; OnPropertyChanged(); } }
    }

    // Kept for backwards compatibility with project serialization / Clone()
    internal void ResetState() { }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
