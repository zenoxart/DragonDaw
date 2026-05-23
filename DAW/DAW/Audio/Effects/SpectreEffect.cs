using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// Spectre — Parallel Multiband Saturator
///
/// Based on the Wavesfactory Spectre concept:
///   • 5 independent bands (Low Shelf, 3× Bell, High Shelf)
///   • Each band extracts a frequency range from the input, saturates it,
///     then adds it back in parallel — gain knob = harmonic intensity, not loudness
///   • 11 saturation algorithms per band
///   • Per-band channel mode: Stereo / Left / Right / Mid / Side
///   • Global: Input, Output, Mix, Saturation Mode (Subtle/Medium/Aggressive), Oversampling
///   • De-Emphasis: compensates the EQ boost after the saturation stage
///
/// Signal flow per band:
///   Input ──► BandEQ (boost-only biquad) ──► diff = EQ − Input
///               │                              │
///               └──────────────────────────────┼──► Saturate(diff) ──► + Input (parallel add)
///                                              │
///                                       channel matrix (M/S decode before, encode after)
/// </summary>
public class SpectreEffect : AudioEffect
{
    // ── Constants ─────────────────────────────────────────────────────────────

    public const int BandCount = 5;

    public static readonly string[] AlgorithmNames =
    [
        "Tube", "Warm Tube", "Solid", "Tape",
        "Diode", "Class B", "Bit", "Digital",
        "Rectify", "Half Rect", "Clean"
    ];

    public static readonly string[] ChannelModeNames =
    [
        "Stereo", "Left", "Right", "Mid", "Side"
    ];

    public static readonly string[] SatModeNames = ["Subtle", "Medium", "Aggressive"];

    // ── Bands ─────────────────────────────────────────────────────────────────

    public SpectreBand[] Bands { get; } =
    [
        new SpectreBand(0, 100f,   BandType.LowShelf,  0.7f),
        new SpectreBand(1, 500f,   BandType.Bell,      1.0f),
        new SpectreBand(2, 2000f,  BandType.Bell,      1.0f),
        new SpectreBand(3, 6000f,  BandType.Bell,      1.0f),
        new SpectreBand(4, 12000f, BandType.HighShelf, 0.7f),
    ];

    // ── Global parameters ─────────────────────────────────────────────────────

    private float _inputGain   = 0f;   // dB
    private float _outputGain  = 0f;   // dB
    private float _mix         = 0.5f; // 0–1
    private int   _satMode     = 0;    // 0=Subtle 1=Medium 2=Aggressive
    private int   _oversampling = 0;   // 0=Off 1=4x 2=16x
    private bool  _deEmphasis  = false;

    public float InputGain
    {
        get => _inputGain;
        set { _inputGain = Math.Clamp(value, -24f, 24f); OnPropertyChanged(); }
    }

    public float OutputGain
    {
        get => _outputGain;
        set { _outputGain = Math.Clamp(value, -24f, 24f); OnPropertyChanged(); }
    }

    public float Mix
    {
        get => _mix;
        set { _mix = Math.Clamp(value, 0f, 1f); OnPropertyChanged(); }
    }

    /// <summary>0 = Subtle, 1 = Medium, 2 = Aggressive</summary>
    public int SatMode
    {
        get => _satMode;
        set { _satMode = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
    }

    /// <summary>0 = Off, 1 = 4×, 2 = 16×</summary>
    public int Oversampling
    {
        get => _oversampling;
        set { _oversampling = Math.Clamp(value, 0, 2); OnPropertyChanged(); }
    }

    /// <summary>Compensate EQ boost post-saturation to keep tonal balance.</summary>
    public bool DeEmphasis
    {
        get => _deEmphasis;
        set { _deEmphasis = value; OnPropertyChanged(); }
    }

    // ── Internal DSP state ────────────────────────────────────────────────────

    // Per-band per-channel biquad states (2 channels = L + R after M/S decode)
    private readonly BiquadState[,] _eqState  = new BiquadState[BandCount, 2];
    private readonly BiquadState[,] _deEqState = new BiquadState[BandCount, 2];
    private int _lastSr;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SpectreEffect()
    {
        Name = "Spectre";
        // Assign band property-changed notifications
        foreach (var b in Bands)
            b.PropertyChanged += (_, _) => OnPropertyChanged("Band");
    }

    public override string EffectType => "Spectre";
    public override string Icon       => "✨";

    // ── Processing ────────────────────────────────────────────────────────────

    public override void ProcessSamples(float[] buffer, int offset, int count,
                                        int sampleRate, int channels)
    {
        if (!IsEnabled || channels < 1) return;

        if (sampleRate != _lastSr)
        {
            _lastSr = sampleRate;
            InvalidateCoefficients();
        }

        // Global gain factors
        float inGain  = MathF.Pow(10f, _inputGain  / 20f);
        float outGain = MathF.Pow(10f, _outputGain / 20f);
        float wet     = _mix;
        float dry     = 1f - wet;

        // Intensity multiplier from SatMode
        float intensity = _satMode switch { 1 => 2.0f, 2 => 4.5f, _ => 1.0f };

        int frames = count / channels;

        for (int f = 0; f < frames; f++)
        {
            int idx = offset + f * channels;
            float xL = buffer[idx]     * inGain;
            float xR = channels > 1 ? buffer[idx + 1] * inGain : xL;

            // M/S decode
            float xM = (xL + xR) * 0.5f;
            float xS = (xL - xR) * 0.5f;

            // Accumulate saturated harmonics
            float addL = 0f, addR = 0f;

            for (int b = 0; b < BandCount; b++)
            {
                var band = Bands[b];
                if (!band.Enabled || band.Gain < 0.01f) continue;

                EnsureBandCoefficients(band, b, sampleRate);

                // Choose channel to process
                float srcA, srcB;
                switch (band.ChannelMode)
                {
                    case 1: srcA = xL;  srcB = xL;  break; // Left
                    case 2: srcA = xR;  srcB = xR;  break; // Right
                    case 3: srcA = xM;  srcB = xM;  break; // Mid
                    case 4: srcA = xS;  srcB = xS;  break; // Side
                    default: srcA = xL; srcB = xR;  break; // Stereo
                }

                // EQ output (boost-only biquad)
                float eqA = ProcessBiquad(srcA, ref _eqState[b, 0], band.Coeffs);
                float eqB = ProcessBiquad(srcB, ref _eqState[b, 1], band.Coeffs);

                // Difference = what the EQ adds
                float diffA = eqA - srcA;
                float diffB = eqB - srcB;

                // De-emphasis: subtract EQ boost from dry to compensate
                if (_deEmphasis)
                {
                    float deA = ProcessBiquad(srcA, ref _deEqState[b, 0], band.Coeffs) - srcA;
                    float deB = ProcessBiquad(srcB, ref _deEqState[b, 1], band.Coeffs) - srcB;
                    diffA -= deA * 0.5f;
                    diffB -= deB * 0.5f;
                }

                // Saturate the difference signal
                float satAmt = band.Gain / 24f * intensity; // 0–1 normalized
                float satA = ApplyAlgorithm(diffA, band.Algorithm, satAmt);
                float satB = ApplyAlgorithm(diffB, band.Algorithm, satAmt);

                // Route back to L/R
                switch (band.ChannelMode)
                {
                    case 1: addL += satA;                break; // Left
                    case 2: addR += satA;                break; // Right (srcB=xR)
                    case 3: addL += satA; addR += satA;  break; // Mid → both
                    case 4: addL += satA; addR -= satA;  break; // Side → L+, R-
                    default: addL += satA; addR += satB; break; // Stereo
                }
            }

            // Mix dry + harmonics
            float outL = (xL * dry + (xL + addL) * wet) * outGain;
            float outR = (xR * dry + (xR + addR) * wet) * outGain;

            buffer[idx] = Clamp(outL);
            if (channels > 1) buffer[idx + 1] = Clamp(outR);
        }
    }

    // ── Saturation algorithms ─────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ApplyAlgorithm(float x, int alg, float amt)
    {
        if (MathF.Abs(amt) < 1e-5f) return 0f;

        return alg switch
        {
            0  => TubeSat(x, amt),
            1  => WarmTubeSat(x, amt),
            2  => SolidSat(x, amt),
            3  => TapeSat(x, amt),
            4  => DiodeSat(x, amt),
            5  => ClassBSat(x, amt),
            6  => BitSat(x, amt),
            7  => DigitalSat(x, amt),
            8  => RectifySat(x, amt),
            9  => HalfRectSat(x, amt),
            10 => x * amt,   // Clean = linear parallel EQ
            _  => TubeSat(x, amt)
        };
    }

    // Tube: 2nd harmonic dominant (asymmetric tanh)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TubeSat(float x, float amt)
    {
        float drive = 1f + amt * 4f;
        float s = x * drive;
        float y = s / (1f + MathF.Abs(s)) + 0.1f * s * s * MathF.Sign(s) * 0.3f;
        return y / drive * amt;
    }

    // Warm Tube: stronger 2nd + gentle 3rd
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float WarmTubeSat(float x, float amt)
    {
        float drive = 1f + amt * 3f;
        float s = x * drive;
        float y = TanhFast(s) * 0.7f + (s - s * s * s * 0.15f) * 0.3f;
        return y / drive * amt;
    }

    // Solid: FET/transistor (symmetric hard-knee)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SolidSat(float x, float amt)
    {
        float drive = 1f + amt * 5f;
        float s = x * drive;
        float y = s / MathF.Sqrt(1f + s * s);
        return y / drive * amt;
    }

    // Tape: soft saturation with high-frequency limiting
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TapeSat(float x, float amt)
    {
        float drive = 1f + amt * 2.5f;
        float s = x * drive;
        // Soft clip that preserves transients
        float y = MathF.Atan(s * 1.2f) * (2f / MathF.PI);
        return y / drive * amt;
    }

    // Diode: hard asymmetric clipping (harsh, more even)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DiodeSat(float x, float amt)
    {
        float drive = 1f + amt * 6f;
        float s = x * drive;
        // Hard clip positive more than negative → asymmetric
        float y = s > 0.6f ? 0.6f + (s - 0.6f) * 0.1f :
                  s < -0.9f ? -0.9f + (s + 0.9f) * 0.1f : s;
        return y / drive * amt;
    }

    // Class B: crossover distortion (odd harmonics, "hollow" mid)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ClassBSat(float x, float amt)
    {
        float drive = 1f + amt * 3f;
        float s = x * drive;
        // Simulate crossover notch around zero crossing
        float dead = 0.15f * amt;
        float y = MathF.Abs(s) > dead ? MathF.Sign(s) * (MathF.Abs(s) - dead) : 0f;
        return y / drive * amt;
    }

    // Bit: stepped quantization (bit-crusher character)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float BitSat(float x, float amt)
    {
        float bits = MathF.Max(2f, 16f - amt * 13f); // 16→3 bits
        float steps = MathF.Pow(2f, bits);
        float q = MathF.Round(x * steps) / steps;
        return (q - x) * amt * 2f; // return the quantization noise
    }

    // Digital: hard symmetric clip (closest to clipping distortion)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DigitalSat(float x, float amt)
    {
        float drive = 1f + amt * 8f;
        float s = x * drive;
        float thresh = 1f - amt * 0.5f;
        float y = Math.Clamp(s, -thresh, thresh);
        return (y - s * 0.3f) / drive * amt;
    }

    // Rectify: full-wave (both half-cycles positive → halves pitch)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float RectifySat(float x, float amt)
    {
        float drive = 1f + amt * 3f;
        float s = x * drive;
        float y = MathF.Abs(s) - MathF.Abs(s * 0.5f); // add subharmonics
        return y / drive * amt;
    }

    // Half rectify: only positive half-cycles pass
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HalfRectSat(float x, float amt)
    {
        float drive = 1f + amt * 3f;
        float s = x * drive;
        float y = MathF.Max(0f, s) * 0.7f; // one-sided
        return y / drive * amt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TanhFast(float x)
    {
        float x2 = x * x;
        return x * (27f + x2) / (27f + 9f * x2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp(float v) => v > 2f ? 2f : v < -2f ? -2f : v;

    // ── Biquad EQ ─────────────────────────────────────────────────────────────

    private void InvalidateCoefficients()
    {
        foreach (var b in Bands) b.CoeffsDirty = true;
    }

    private void EnsureBandCoefficients(SpectreBand band, int idx, int sr)
    {
        if (!band.CoeffsDirty) return;
        band.Coeffs = ComputeBiquad(band, sr);
        band.CoeffsDirty = false;
    }

    private static BiquadCoeffs ComputeBiquad(SpectreBand band, int sr)
    {
        double w0 = 2.0 * Math.PI * band.Frequency / sr;
        double cosW = Math.Cos(w0);
        double sinW = Math.Sin(w0);
        double alpha = sinW / (2.0 * band.Q);
        double A = Math.Pow(10.0, band.Gain / 40.0);

        double b0, b1, b2, a0, a1, a2;

        if (band.BandType == BandType.LowShelf)
        {
            double sqrtA = Math.Sqrt(A);
            b0 = A * ((A + 1) - (A - 1) * cosW + 2 * sqrtA * alpha);
            b1 = 2 * A * ((A - 1) - (A + 1) * cosW);
            b2 = A * ((A + 1) - (A - 1) * cosW - 2 * sqrtA * alpha);
            a0 = (A + 1) + (A - 1) * cosW + 2 * sqrtA * alpha;
            a1 = -2 * ((A - 1) + (A + 1) * cosW);
            a2 = (A + 1) + (A - 1) * cosW - 2 * sqrtA * alpha;
        }
        else if (band.BandType == BandType.HighShelf)
        {
            double sqrtA = Math.Sqrt(A);
            b0 = A * ((A + 1) + (A - 1) * cosW + 2 * sqrtA * alpha);
            b1 = -2 * A * ((A - 1) + (A + 1) * cosW);
            b2 = A * ((A + 1) + (A - 1) * cosW - 2 * sqrtA * alpha);
            a0 = (A + 1) - (A - 1) * cosW + 2 * sqrtA * alpha;
            a1 = 2 * ((A - 1) - (A + 1) * cosW);
            a2 = (A + 1) - (A - 1) * cosW - 2 * sqrtA * alpha;
        }
        else // Bell
        {
            b0 = 1 + alpha * A;
            b1 = -2 * cosW;
            b2 = 1 - alpha * A;
            a0 = 1 + alpha / A;
            a1 = -2 * cosW;
            a2 = 1 - alpha / A;
        }

        return new BiquadCoeffs(
            (float)(b0 / a0), (float)(b1 / a0), (float)(b2 / a0),
            (float)(a1 / a0), (float)(a2 / a0));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ProcessBiquad(float x, ref BiquadState s, BiquadCoeffs c)
    {
        float y = c.B0 * x + c.B1 * s.X1 + c.B2 * s.X2
                            - c.A1 * s.Y1 - c.A2 * s.Y2;
        s.X2 = s.X1; s.X1 = x;
        s.Y2 = s.Y1; s.Y1 = y;
        return y;
    }

    public override void Reset()
    {
        for (int b = 0; b < BandCount; b++)
            for (int c = 0; c < 2; c++)
            { _eqState[b, c] = default; _deEqState[b, c] = default; }
        _lastSr = 0;
        InvalidateCoefficients();
    }
}

// ── Supporting types ──────────────────────────────────────────────────────────

public enum BandType { LowShelf, Bell, HighShelf }

public class SpectreBand : System.ComponentModel.INotifyPropertyChanged
{
    public int Index { get; }
    public BandType BandType { get; }

    private float _frequency;
    private float _gain;
    private float _q;
    private int   _algorithm;   // 0–10
    private int   _channelMode; // 0=Stereo 1=L 2=R 3=Mid 4=Side
    private bool  _enabled = true;

    internal BiquadCoeffs Coeffs;
    internal bool CoeffsDirty = true;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? n = null)
    { CoeffsDirty = true; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n)); }

    public SpectreBand(int index, float freq, BandType type, float q)
    {
        Index = index;
        BandType = type;
        _frequency = freq;
        _q = q;
        _gain = 0f;
        _algorithm = 0;
        _channelMode = 0;
    }

    public float Frequency { get => _frequency; set { _frequency = Math.Clamp(value, 20f, 20000f); Notify(); } }
    public float Gain       { get => _gain;      set { _gain      = Math.Clamp(value, 0f, 24f);    Notify(); } }
    public float Q          { get => _q;         set { _q         = Math.Clamp(value, 0.1f, 10f);  Notify(); } }

    /// <summary>0=Tube 1=WarmTube 2=Solid 3=Tape 4=Diode 5=ClassB 6=Bit 7=Digital 8=Rectify 9=HalfRect 10=Clean</summary>
    public int Algorithm   { get => _algorithm;   set { _algorithm   = Math.Clamp(value, 0, 10); Notify(); } }

    /// <summary>0=Stereo 1=Left 2=Right 3=Mid 4=Side</summary>
    public int ChannelMode { get => _channelMode; set { _channelMode = Math.Clamp(value, 0, 4);  Notify(); } }

    public bool Enabled    { get => _enabled;     set { _enabled = value; Notify(); } }

    public string DisplayName => BandType switch
    {
        BandType.LowShelf  => $"Lo Shelf",
        BandType.HighShelf => $"Hi Shelf",
        _                  => $"Band {Index}"
    };
}

// ── DSP structs ───────────────────────────────────────────────────────────────

internal struct BiquadCoeffs
{
    public float B0, B1, B2, A1, A2;
    public BiquadCoeffs(float b0, float b1, float b2, float a1, float a2)
    { B0 = b0; B1 = b1; B2 = b2; A1 = a1; A2 = a2; }
}

internal struct BiquadState
{
    public float X1, X2, Y1, Y2;
}
