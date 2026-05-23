using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using DAW.Audio.Effects;
using DAW.Audio.Effects.DragonParticle;

namespace DAW.ViewModels;

/// <summary>
/// MasterViewModel — the "smart parameter orchestrator" for Dragon Particle.
///
/// The single Amount knob scales all five mastering stages simultaneously,
/// mimicking how the God Particle works: a top engineer's pre-tuned session.
///
/// Amount = 0.0  → bypass-like: only gentle tonal shaping, no compression
/// Amount = 0.3  → polish: subtle glue, warmth, slight clarity
/// Amount = 0.5  → mastered: full chain active, streaming-ready
/// Amount = 1.0  → heavy: maximum processing, aggressive loudness
///
/// The curves are psychoacoustically tuned so each stage enters at the
/// right time: EQ first, then compression, then saturation, then width,
/// then the limiter tightens last.
/// </summary>
public sealed class MasterViewModel : INotifyPropertyChanged
{
    private readonly MasterEffect   _model;
    private readonly DispatcherTimer _meterTimer;

    // ── User parameters ───────────────────────────────────────────────────────
    private double _amount     = 0.3;
    private double _inputGain  = 0.0;
    private double _outputGain = 0.0;
    private double _low        = 0.0;
    private double _mid        = 0.0;
    private double _high       = 0.0;

    public double Amount
    {
        get => _amount;
        set { if (SetField(ref _amount, Math.Clamp(value, 0, 1))) PushAll(); }
    }
    public double InputGain
    {
        get => _inputGain;
        set { if (SetField(ref _inputGain, Math.Clamp(value, -12, 12))) PushGains(); }
    }
    public double OutputGain
    {
        get => _outputGain;
        set { if (SetField(ref _outputGain, Math.Clamp(value, -12, 12))) PushGains(); }
    }
    public double Low
    {
        get => _low;
        set { if (SetField(ref _low, Math.Clamp(value, -6, 6))) PushUserEq(); }
    }
    public double Mid
    {
        get => _mid;
        set { if (SetField(ref _mid, Math.Clamp(value, -6, 6))) PushUserEq(); }
    }
    public double High
    {
        get => _high;
        set { if (SetField(ref _high, Math.Clamp(value, -6, 6))) PushUserEq(); }
    }

    // ── Display strings ───────────────────────────────────────────────────────
    public string AmountStr     => $"{_amount * 100:F0}%";
    public string InputGainStr  => $"{_inputGain:+0.0;-0.0;0.0} dB";
    public string OutputGainStr => $"{_outputGain:+0.0;-0.0;0.0} dB";
    public string LowStr        => $"{_low:+0.0;-0.0;0.0}";
    public string MidStr        => $"{_mid:+0.0;-0.0;0.0}";
    public string HighStr       => $"{_high:+0.0;-0.0;0.0}";

    // Normalised 0–1 for knob rendering
    public double AmountNorm     => _amount;
    public double InputGainNorm  => (_inputGain  + 12) / 24.0;
    public double OutputGainNorm => (_outputGain + 12) / 24.0;
    public double LowNorm        => (_low  + 6) / 12.0;
    public double MidNorm        => (_mid  + 6) / 12.0;
    public double HighNorm       => (_high + 6) / 12.0;

    // Mode label shown below Amount knob
    public string AmountMode => _amount switch
    {
        < 0.15 => "Transparent",
        < 0.35 => "Polish",
        < 0.55 => "Mastered",
        < 0.75 => "Loud",
        _       => "Heavy"
    };

    // ── Metering ──────────────────────────────────────────────────────────────
    private double _meterRms  = 0;
    private double _meterPeak = 0;
    private double _meterLufs = -70;

    public double MeterRms  { get => _meterRms;  private set => SetField(ref _meterRms,  value); }
    public double MeterPeak { get => _meterPeak; private set => SetField(ref _meterPeak, value); }
    public double MeterLufs { get => _meterLufs; private set => SetField(ref _meterLufs, value); }

    public double MeterRmsNorm  => Math.Clamp((_meterRms  + 60) / 60.0, 0, 1);
    public double MeterPeakNorm => Math.Clamp((_meterPeak + 60) / 60.0, 0, 1);
    public double MeterLufsNorm => Math.Clamp((_meterLufs + 60) / 60.0, 0, 1);

    public string MeterRmsStr  => _meterRms  < -59 ? "—" : $"{_meterRms:F1} dB";
    public string MeterPeakStr => _meterPeak < -59 ? "—" : $"{_meterPeak:F1} dB";
    public string MeterLufsStr => _meterLufs < -59 ? "—" : $"{_meterLufs:F1} LU";

    // DAG latency graph for view (not tracked by MasterEffect — returns empty)
    public IReadOnlyList<NodeLatencyInfo> DagLatencies => [];

    // ── Constructor ───────────────────────────────────────────────────────────
    public MasterViewModel(MasterEffect model)
    {
        _model = model;
        PushAll();
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(16) };
        _meterTimer.Tick += PollMetering;
        _meterTimer.Start();
    }

    public void StopMetering() => _meterTimer.Stop();

    // ═══════════════════════════════════════════════════════════════════════════
    //  THE INTELLIGENCE — Amount maps to all five stages
    // ═══════════════════════════════════════════════════════════════════════════

    private void PushAll()
    {
        double a = _amount;

        // ── Stage 1: Pre-EQ ───────────────────────────────────────────────────
        // Tightens sub-bass, lifts low-mid body, carves muddy 200Hz,
        // adds upper-mid presence and airy top-end.
        // All gains scale with Amount so it's subtle at low settings.
        double subCutFreq = a > 0.15 ? 20.0 + Sk(a, 0.10) * 35.0 : 0;  // 0→55Hz HP
        double preLoGain  =  Sk(a, 0.05) * 1.8;    // 0 → +1.8 dB @80Hz (body)
        double preLmGain  =  Sk(a, 0.20) * 1.2;    // 0 → +1.2 dB @200Hz (warmth)
        double preMdGain  = -Sk(a, 0.25) * 1.0;    // 0 → -1.0 dB @2kHz (clear mids)
        double preHmGain  =  Sk(a, 0.15) * 2.0;    // 0 → +2.0 dB @5kHz (presence)
        double preHiGain  =  Sk(a, 0.10) * 2.5;    // 0 → +2.5 dB @12kHz (air)

        _model.SetPreEq(
            (float)subCutFreq,
            (float)preLoGain,
            (float)preLmGain,
            (float)preMdGain,
            (float)preHmGain,
            (float)preHiGain);

        // ── Stage 2: Glue compressor ──────────────────────────────────────────
        // Almost transparent at low Amount — threshold stays high.
        // Gradually engages as Amount rises. Soft knee keeps it musical.
        // Attack/release tighten progressively for more "glue".
        double ratio  = 1.0 + Sk(a, 0.20) * 2.5;         // 1.0 → 3.5
        double thresh = -6.0 - Sk(a, 0.30) * 16.0;       // -6 → -22 dBFS
        double knee   = 8.0 - Sk(a, 0.40) * 4.0;         // 8 → 4 dB (tighter knee)
        double atk    = 0.050 - Sk(a, 0.50) * 0.042;     // 50ms → 8ms
        double rel    = 0.300 - Sk(a, 0.40) * 0.200;     // 300ms → 100ms

        _model.SetGlueComp(
            (float)ratio, (float)thresh,
            (float)atk,   (float)rel,
            (float)knee);

        // ── Stage 3: Harmonic saturation ─────────────────────────────────────
        // Enters only after Amount > 0.20 to keep low settings clean.
        // Even harmonics (warmth) lead at lower settings.
        // Odd harmonics (edge/presence) grow more at higher settings.
        double satDrive = Math.Max(0, (a - 0.20) / 0.80) * 0.65;
        satDrive = Math.Pow(satDrive, 0.60);            // gentle curve onset
        double evenW = 0.72 - Sk(a, 0.50) * 0.28;     // 0.72 → 0.44 (warm → edge)
        double oddW  = 0.28 + Sk(a, 0.50) * 0.28;     // 0.28 → 0.56

        _model.SetSaturation((float)satDrive, (float)evenW, (float)oddW);

        // ── Stage 4: Frequency-dependent stereo widening ──────────────────────
        // Subtle at low amounts. Bass always stays mono (handled in DSP).
        // Maximum +18% width — never sounds fake or phasey.
        double stereoW = 1.0 + Sk(a, 0.35) * 0.18;    // 1.0 → 1.18

        _model.SetStereoWidth((float)stereoW);

        // ── Stage 5: True-peak limiter ────────────────────────────────────────
        // Stays relaxed until Amount > 0.4, then tightens for loudness.
        // Release also tightens so it pumps less at high settings.
        double limThresh = -1.0 + Sk(a, 0.45) * 0.8;  // -1.0 → -0.2 dBFS
        double limRel    = 0.080 - Sk(a, 0.50) * 0.060; // 80ms → 20ms

        _model.SetLimiter((float)limThresh, (float)limRel);

        // Apply gains and user EQ
        PushGains();
        PushUserEq();

        // Notify derived display properties
        OnPropertyChanged(nameof(AmountStr));
        OnPropertyChanged(nameof(AmountMode));
        OnPropertyChanged(nameof(AmountNorm));
    }

    private void PushGains()
    {
        _model.SetInputGain (DbToLin(_inputGain));
        _model.SetOutputGain(DbToLin(_outputGain));
    }

    private void PushUserEq()
        => _model.SetUserEq((float)_low, (float)_mid, (float)_high);

    // ── Metering ──────────────────────────────────────────────────────────────
    private void PollMetering(object? s, EventArgs e)
    {
        double rms  = _model.MeterRmsLinear  > 1e-7f ? 20 * Math.Log10(_model.MeterRmsLinear)  : -70;
        double peak = _model.MeterPeakLinear > 1e-7f ? 20 * Math.Log10(_model.MeterPeakLinear) : -70;
        double lufs = _model.MeterLufsPower  > 1e-10f
            ? -0.691 + 10 * Math.Log10(_model.MeterLufsPower / 2.0)
            : -70;

        MeterRms  = rms;
        MeterPeak = peak;
        MeterLufs = lufs;

        OnPropertyChanged(nameof(MeterRmsNorm));
        OnPropertyChanged(nameof(MeterPeakNorm));
        OnPropertyChanged(nameof(MeterLufsNorm));
        OnPropertyChanged(nameof(MeterRmsStr));
        OnPropertyChanged(nameof(MeterPeakStr));
        OnPropertyChanged(nameof(MeterLufsStr));
    }

    // ── Smoothstep soft-knee helper ───────────────────────────────────────────
    /// <summary>
    /// S-curve from 0→1 where onset is delayed until x > knee.
    /// Sk(0.3, 0.2) = smoothstep((0.3-0.2)/(1-0.2)) = smoothstep(0.125) ≈ 0.05
    /// </summary>
    private static double Sk(double x, double knee)
    {
        if (x <= knee) return 0;
        if (x >= 1)    return 1;
        double t = (x - knee) / (1 - knee);
        return t * t * (3 - 2 * t);
    }

    private static float DbToLin(double db) => (float)Math.Pow(10, db / 20.0);

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
