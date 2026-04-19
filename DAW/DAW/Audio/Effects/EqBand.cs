using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// Represents a single band of the 7-band parametric EQ.
/// Each band has its own frequency, gain, Q factor, and filter mode.
/// </summary>
public sealed class EqBand : INotifyPropertyChanged
{
    private double _gain;
    private double _frequency;
    private double _q = 1.0;
    private EqBandMode _mode = EqBandMode.Peaking;
    private bool _isEnabled = true;

    // Biquad filter state (2 values per channel, 2 channels max)
    internal readonly double[] State = new double[4];

    public EqBand(int number, double defaultFrequency, EqBandMode defaultMode = EqBandMode.Peaking)
    {
        Number = number;
        _frequency = defaultFrequency;
        _mode = defaultMode;
    }

    /// <summary>Band number (1–7).</summary>
    public int Number { get; }

    /// <summary>Whether this band is active.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChanged(); } }
    }

    /// <summary>Gain in dB (−18 to +18). Ignored for LowCut/HighCut/Notch/BandPass.</summary>
    public double Gain
    {
        get => _gain;
        set { var v = Math.Clamp(value, -18, 18); if (_gain != v) { _gain = v; OnPropertyChanged(); } }
    }

    /// <summary>Center / cutoff frequency in Hz (20–20 000).</summary>
    public double Frequency
    {
        get => _frequency;
        set { var v = Math.Clamp(value, 20, 20000); if (_frequency != v) { _frequency = v; OnPropertyChanged(); } }
    }

    /// <summary>Q factor / bandwidth (0.05–30).</summary>
    public double Q
    {
        get => _q;
        set { var v = Math.Clamp(value, 0.05, 30); if (_q != v) { _q = v; OnPropertyChanged(); } }
    }

    /// <summary>Filter type for this band.</summary>
    public EqBandMode Mode
    {
        get => _mode;
        set { if (_mode != value) { _mode = value; OnPropertyChanged(); } }
    }

    internal void ResetState() => Array.Clear(State);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
