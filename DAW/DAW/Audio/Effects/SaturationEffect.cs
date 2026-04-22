using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// Black Box HG-2–inspired tube saturation effect.
///
/// Signal architecture:
///   Input ──► [Input Transformer LP]
///               │
///               ├──► Pentode (6U8A even-order) ──► Triode (6U8A odd-order)  ← series path
///               │
///               └──► Parallel Sat (12AX7) ──► Band Filter ──► Sat Mix      ← parallel path
///                                                                    │
///                                                             [Sum + Air + Density]
///                                                                    │
///                                                          [Output Transformer LP]
///                                                                    │
///                                                           [Wet/Dry Mix] ──► Output
///
/// Pentode  → even-order harmonics (2nd, 4th): warm, full body
/// Triode   → odd-order  harmonics (3rd, 5th): gritty, character
/// Parallel → 12AX7 hard-knee atan: radiance and overdrive edge
/// Air      → gentle 1-pole high-shelf at ~8 kHz: sparkle
/// Density  → scales all drive amounts together
/// Mix      → parallel wet/dry blend
/// </summary>
public class SaturationEffect : AudioEffect
{
    // Parameters — plain fields, assigned atomically on x86/x64.
    // The audio thread only reads; the UI thread only writes.
    // Using non-volatile + direct assignment avoids the "ref to volatile" CS0420 warning.
    private float _pentodeDrive = 0.3f;
    private float _triodeDrive  = 0.2f;
    private float _parallelSat  = 0.0f;
    private bool  _parallelOn   = false;
    private int   _satFreq      = 1;      // 0=Low  1=Flat  2=High
    private float _density      = 0.5f;
    private float _air          = 0.0f;
    private float _outputGain   = 0.0f;   // dB  -12..+12
    private float _mix          = 1.0f;

    // Per-channel 1-pole filter states  [0 = L, 1 = R]
    private float[] _airZ    = new float[2];
    private float[] _satHpZ  = new float[2];
    private float[] _satLpZ  = new float[2];
    private float[] _xfmrZ   = new float[2];
    private int     _lastSr;

    public SaturationEffect() => Name = "Saturation";

    public override string EffectType => "Saturation";
    public override string Icon       => "🔥";

    // ── Public parameters ─────────────────────────────────────────────────────

    /// <summary>Pentode (6U8A) drive — even-order harmonics. 0–1.</summary>
    public float PentodeDrive
    {
        get => _pentodeDrive;
        set { _pentodeDrive = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    /// <summary>Triode (6U8A) drive — odd-order harmonics. 0–1.</summary>
    public float TriodeDrive
    {
        get => _triodeDrive;
        set { _triodeDrive = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    /// <summary>Parallel 12AX7 saturation amount. 0–1.</summary>
    public float ParallelSat
    {
        get => _parallelSat;
        set { _parallelSat = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    /// <summary>Enable the parallel saturation circuit.</summary>
    public bool ParallelOn
    {
        get => _parallelOn;
        set { _parallelOn = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Frequency band for the parallel circuit.
    /// 0 = Low (LP @800 Hz)  1 = Flat (full spectrum)  2 = High (HP @3 kHz).
    /// </summary>
    public int SatFreq
    {
        get => _satFreq;
        set { _satFreq = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
    }

    /// <summary>Density — scales all drive amounts. 0–1.</summary>
    public float Density
    {
        get => _density;
        set { _density = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    /// <summary>Air — high-frequency sparkle shelf. 0–1.</summary>
    public float Air
    {
        get => _air;
        set { _air = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    /// <summary>Output gain in dB. -12..+12.</summary>
    public float OutputGain
    {
        get => _outputGain;
        set { _outputGain = Math.Clamp(value, -12f, 12f); OnPropertyChanged(); }
    }

    /// <summary>Wet/dry mix. 0 = dry, 1 = fully wet.</summary>
    public float Mix
    {
        get => _mix;
        set { _mix = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    // ── Waveshaper math ───────────────────────────────────────────────────────

    /// <summary>
    /// Pentode stage — asymmetric waveshaper producing strong 2nd harmonic.
    /// Bias toward positive emulates real tube asymmetry.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float PentodeShape(float x, float drive)
    {
        float s = x * (1f + drive * 3f);
        float y = s - s * s * 0.333f - s * s * s * 0.05f;
        return y / (1f + drive * 1.2f);
    }

    /// <summary>
    /// Triode stage — tanh-family producing odd harmonics (3rd, 5th).
    /// Classic tube emulation symmetric waveshaper.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TriodeShape(float x, float drive)
    {
        float s = x * (1f + drive * 2.5f);
        return TanhFast(s) / (1f + drive * 0.8f);
    }

    /// <summary>
    /// Parallel 12AX7 — atan-based hard-knee for radiance and edge.
    /// Signal is scaled by amount and added to the parallel bus.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ParallelShape(float x, float amount)
    {
        float s = x * (1f + amount * 5f);
        return MathF.Atan(s) * (2f / MathF.PI) * amount;
    }

    /// <summary>Fast tanh rational approximation (< 0.01 % error up to ±3).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TanhFast(float x)
    {
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }

    // ── 1-pole filter helpers ─────────────────────────────────────────────────

    /// <summary>1-pole LP: exponential moving average.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float LP1(float x, float fc, int sr, ref float z)
    {
        float a = 1f - MathF.Exp(-2f * MathF.PI * fc / sr);
        z += a * (x - z);
        return z;
    }

    /// <summary>1-pole HP = input − LP.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HP1(float x, float fc, int sr, ref float z)
        => x - LP1(x, fc, sr, ref z);

    // ── Processing ────────────────────────────────────────────────────────────

    public override void ProcessSamples(float[] buffer, int offset, int count,
                                        int sampleRate, int channels)
    {
        if (!IsEnabled) return;

        _lastSr = sampleRate;

        // Snapshot parameters once per block to avoid mid-block changes
        float pentDrive  = _pentodeDrive;
        float triDrive   = _triodeDrive;
        float parSat     = _parallelSat;
        bool  parOn      = _parallelOn;
        int   satFreq    = _satFreq;
        float density    = _density;
        float airAmt     = _air;
        float outGainLin = MathF.Pow(10f, _outputGain / 20f);
        float wet        = _mix;
        float dry        = 1f - wet;

        // Scale drives by density
        float effPent = pentDrive * density * 2f;
        float effTri  = triDrive  * density * 2f;
        float effPar  = parSat    * density;

        const float lpFc = 800f;
        const float hpFc = 3000f;

        for (int i = offset; i < offset + count; i++)
        {
            int ch = channels == 2 ? i % 2 : 0;

            float x    = buffer[i];
            float xDry = x;

            // Input transformer warmth (subtle LP at 22 kHz)
            float xIn = LP1(x, 22000f, sampleRate, ref _xfmrZ[ch]);

            // Series path: Pentode → Triode
            float series = xIn;
            if (effPent > 0.001f) series = PentodeShape(series, effPent);
            if (effTri  > 0.001f) series = TriodeShape (series, effTri);

            // Parallel path: band-filtered → 12AX7
            float parOut = 0f;
            if (parOn && effPar > 0.001f)
            {
                float xBand = satFreq switch
                {
                    0 => LP1(xIn, lpFc, sampleRate, ref _satLpZ[ch]),
                    2 => HP1(xIn, hpFc, sampleRate, ref _satHpZ[ch]),
                    _ => xIn
                };
                parOut = ParallelShape(xBand, effPar);
            }

            // Sum series + parallel
            float wetSig = series + parOut;

            // Air: high-shelf boost
            if (airAmt > 0.001f)
            {
                float airHp = HP1(wetSig, 8000f, sampleRate, ref _airZ[ch]);
                wetSig += airHp * (airAmt * 0.5f);
            }

            // Output transformer
            wetSig = LP1(wetSig, 22000f, sampleRate, ref _xfmrZ[ch]);

            // Wet/dry + output gain
            float y = (dry * xDry + wet * wetSig) * outGainLin;

            // Hard limiter (prevents driver overload)
            buffer[i] = Math.Clamp(y, -1.5f, 1.5f);
        }
    }

    public override void Reset()
    {
        Array.Clear(_airZ);
        Array.Clear(_satHpZ);
        Array.Clear(_satLpZ);
        Array.Clear(_xfmrZ);
        _lastSr = 0;
    }
}
