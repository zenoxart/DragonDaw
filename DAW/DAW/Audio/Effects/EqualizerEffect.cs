namespace DAW.Audio.Effects;

/// <summary>
/// 7-Band Parametric EQ inspired by FL Studio Parametric EQ 2.
/// Each band supports Peaking, LowShelf, HighShelf, LowCut, HighCut, Notch and BandPass modes.
/// Includes FFT-based spectrum analysis for real-time visualization.
/// </summary>
public class EqualizerEffect : AudioEffect
{
    public const int BandCount = 7;
    private const int FftSize = 2048;
    private const int FftHalf = FftSize / 2;

    /// <summary>The 7 EQ bands (index 0 = Band 1, …, index 6 = Band 7).</summary>
    public EqBand[] Bands { get; }

    // ── FFT spectrum analyzer ─────────────────────────────────────────────

    private readonly float[] _fftBuffer = new float[FftSize];
    private int _fftPos;
    private readonly double[] _fftWindow = new double[FftSize];
    private readonly double[] _spectrumSmoothed = new double[FftHalf];

    /// <summary>
    /// Smoothed magnitude spectrum data (linear scale, FftSize/2 bins).
    /// Read from UI thread for visualization.
    /// </summary>
    public double[]? SpectrumData { get; private set; }

    /// <summary>Last known sample rate from ProcessSamples. Used by the UI for frequency mapping.</summary>
    public double LastSampleRate { get; private set; } = 44100;

    public EqualizerEffect()
    {
        Name = "Parametric EQ";

        // Hann window for FFT
        for (int i = 0; i < FftSize; i++)
            _fftWindow[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

        // Default band layout similar to FL Parametric EQ 2
        Bands =
        [
            new EqBand(1,    80, EqBandMode.LowShelf),
            new EqBand(2,   200, EqBandMode.Peaking),
            new EqBand(3,   600, EqBandMode.Peaking),
            new EqBand(4,  1500, EqBandMode.Peaking),
            new EqBand(5,  4000, EqBandMode.Peaking),
            new EqBand(6,  8000, EqBandMode.Peaking),
            new EqBand(7, 16000, EqBandMode.HighShelf),
        ];
    }

    public override string EffectType => "Equalizer";
    public override string Icon => "📊";

    // ── Legacy compatibility properties (band 1/4/7 map to old Low/Mid/High) ──
    // These allow the reflection-based Clone() and older project files to still work.

    public double LowGain     { get => Bands[0].Gain;      set => Bands[0].Gain = value; }
    public double MidGain     { get => Bands[3].Gain;      set => Bands[3].Gain = value; }
    public double HighGain    { get => Bands[6].Gain;      set => Bands[6].Gain = value; }
    public double LowFrequency  { get => Bands[0].Frequency; set => Bands[0].Frequency = value; }
    public double MidFrequency  { get => Bands[3].Frequency; set => Bands[3].Frequency = value; }
    public double HighFrequency { get => Bands[6].Frequency; set => Bands[6].Frequency = value; }
    public double MidQ          { get => Bands[3].Q;         set => Bands[3].Q = value; }

    // ── Band accessor helpers for serialization ──

    public double Band1Gain { get => Bands[0].Gain; set => Bands[0].Gain = value; }
    public double Band1Freq { get => Bands[0].Frequency; set => Bands[0].Frequency = value; }
    public double Band1Q    { get => Bands[0].Q; set => Bands[0].Q = value; }
    public int    Band1Mode { get => (int)Bands[0].Mode; set => Bands[0].Mode = (EqBandMode)value; }

    public double Band2Gain { get => Bands[1].Gain; set => Bands[1].Gain = value; }
    public double Band2Freq { get => Bands[1].Frequency; set => Bands[1].Frequency = value; }
    public double Band2Q    { get => Bands[1].Q; set => Bands[1].Q = value; }
    public int    Band2Mode { get => (int)Bands[1].Mode; set => Bands[1].Mode = (EqBandMode)value; }

    public double Band3Gain { get => Bands[2].Gain; set => Bands[2].Gain = value; }
    public double Band3Freq { get => Bands[2].Frequency; set => Bands[2].Frequency = value; }
    public double Band3Q    { get => Bands[2].Q; set => Bands[2].Q = value; }
    public int    Band3Mode { get => (int)Bands[2].Mode; set => Bands[2].Mode = (EqBandMode)value; }

    public double Band4Gain { get => Bands[3].Gain; set => Bands[3].Gain = value; }
    public double Band4Freq { get => Bands[3].Frequency; set => Bands[3].Frequency = value; }
    public double Band4Q    { get => Bands[3].Q; set => Bands[3].Q = value; }
    public int    Band4Mode { get => (int)Bands[3].Mode; set => Bands[3].Mode = (EqBandMode)value; }

    public double Band5Gain { get => Bands[4].Gain; set => Bands[4].Gain = value; }
    public double Band5Freq { get => Bands[4].Frequency; set => Bands[4].Frequency = value; }
    public double Band5Q    { get => Bands[4].Q; set => Bands[4].Q = value; }
    public int    Band5Mode { get => (int)Bands[4].Mode; set => Bands[4].Mode = (EqBandMode)value; }

    public double Band6Gain { get => Bands[5].Gain; set => Bands[5].Gain = value; }
    public double Band6Freq { get => Bands[5].Frequency; set => Bands[5].Frequency = value; }
    public double Band6Q    { get => Bands[5].Q; set => Bands[5].Q = value; }
    public int    Band6Mode { get => (int)Bands[5].Mode; set => Bands[5].Mode = (EqBandMode)value; }

    public double Band7Gain { get => Bands[6].Gain; set => Bands[6].Gain = value; }
    public double Band7Freq { get => Bands[6].Frequency; set => Bands[6].Frequency = value; }
    public double Band7Q    { get => Bands[6].Q; set => Bands[6].Q = value; }
    public int    Band7Mode { get => (int)Bands[6].Mode; set => Bands[6].Mode = (EqBandMode)value; }

    // ── Audio processing ──────────────────────────────────────────────────────

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        LastSampleRate = sampleRate;

        if (!IsEnabled) return;

        int endIndex = offset + count;
        for (int i = offset; i < endIndex; i += channels)
        {
            for (int ch = 0; ch < channels && (i + ch) < endIndex; ch++)
            {
                double sample = buffer[i + ch];

                for (int b = 0; b < BandCount; b++)
                {
                    var band = Bands[b];
                    if (!band.IsEnabled) continue;
                    if (band.Mode == EqBandMode.Peaking && Math.Abs(band.Gain) < 0.1) continue;

                    sample = ProcessBiquad(sample, sampleRate, band, ch * 2);
                }

                buffer[i + ch] = (float)Math.Clamp(sample, -1.0, 1.0);
            }

            // Feed mono-mix to FFT buffer (post-EQ for spectrum display)
            float monoSample = buffer[i];
            if (channels == 2 && (i + 1) < endIndex)
                monoSample = (buffer[i] + buffer[i + 1]) * 0.5f;

            _fftBuffer[_fftPos++] = monoSample;
            if (_fftPos >= FftSize)
            {
                ComputeSpectrum();
                _fftPos = 0;
            }
        }
    }

    /// <summary>Simple in-place radix-2 FFT, then smooth into spectrum data.</summary>
    private void ComputeSpectrum()
    {
        // Apply window and copy to complex arrays
        var real = new double[FftSize];
        var imag = new double[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            real[i] = _fftBuffer[i] * _fftWindow[i];
            imag[i] = 0;
        }

        // Bit-reversal permutation
        int n = FftSize;
        int j = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
            int k = n >> 1;
            while (k <= j) { j -= k; k >>= 1; }
            j += k;
        }

        // FFT
        for (int size = 2; size <= n; size <<= 1)
        {
            int halfSize = size >> 1;
            double angle = -2.0 * Math.PI / size;
            double wReal = Math.Cos(angle), wImag = Math.Sin(angle);
            for (int i = 0; i < n; i += size)
            {
                double curReal = 1, curImag = 0;
                for (int k = 0; k < halfSize; k++)
                {
                    int idx1 = i + k, idx2 = i + k + halfSize;
                    double tReal = curReal * real[idx2] - curImag * imag[idx2];
                    double tImag = curReal * imag[idx2] + curImag * real[idx2];
                    real[idx2] = real[idx1] - tReal;
                    imag[idx2] = imag[idx1] - tImag;
                    real[idx1] += tReal;
                    imag[idx1] += tImag;
                    double newCurReal = curReal * wReal - curImag * wImag;
                    curImag = curReal * wImag + curImag * wReal;
                    curReal = newCurReal;
                }
            }
        }

        // Magnitude + smoothing (lower = more responsive)
        const double smoothing = 0.55;
        for (int i = 0; i < FftHalf; i++)
        {
            double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / FftSize * 2.0;
            _spectrumSmoothed[i] = _spectrumSmoothed[i] * smoothing + mag * (1.0 - smoothing);
        }

        // Publish snapshot (safe for UI read)
        var snapshot = new double[FftHalf];
        Array.Copy(_spectrumSmoothed, snapshot, FftHalf);
        SpectrumData = snapshot;
    }

    // ── Biquad filter implementation ──────────────────────────────────────────

    private static double ProcessBiquad(double input, int sampleRate, EqBand band, int stateOffset)
    {
        double freq = band.Frequency;
        double gainDb = band.Gain;
        double q = band.Q;

        double omega = 2.0 * Math.PI * freq / sampleRate;
        double sinOmega = Math.Sin(omega);
        double cosOmega = Math.Cos(omega);
        double alpha = sinOmega / (2.0 * q);
        double A = Math.Pow(10, gainDb / 40.0);

        double b0, b1, b2, a0, a1, a2;

        switch (band.Mode)
        {
            case EqBandMode.Peaking:
                b0 = 1 + alpha * A;
                b1 = -2 * cosOmega;
                b2 = 1 - alpha * A;
                a0 = 1 + alpha / A;
                a1 = -2 * cosOmega;
                a2 = 1 - alpha / A;
                break;

            case EqBandMode.LowShelf:
            {
                var sqrtA = Math.Sqrt(A);
                b0 = A * ((A + 1) - (A - 1) * cosOmega + 2 * sqrtA * alpha);
                b1 = 2 * A * ((A - 1) - (A + 1) * cosOmega);
                b2 = A * ((A + 1) - (A - 1) * cosOmega - 2 * sqrtA * alpha);
                a0 = (A + 1) + (A - 1) * cosOmega + 2 * sqrtA * alpha;
                a1 = -2 * ((A - 1) + (A + 1) * cosOmega);
                a2 = (A + 1) + (A - 1) * cosOmega - 2 * sqrtA * alpha;
                break;
            }

            case EqBandMode.HighShelf:
            {
                var sqrtA = Math.Sqrt(A);
                b0 = A * ((A + 1) + (A - 1) * cosOmega + 2 * sqrtA * alpha);
                b1 = -2 * A * ((A - 1) + (A + 1) * cosOmega);
                b2 = A * ((A + 1) + (A - 1) * cosOmega - 2 * sqrtA * alpha);
                a0 = (A + 1) - (A - 1) * cosOmega + 2 * sqrtA * alpha;
                a1 = 2 * ((A - 1) - (A + 1) * cosOmega);
                a2 = (A + 1) - (A - 1) * cosOmega - 2 * sqrtA * alpha;
                break;
            }

            case EqBandMode.LowCut: // High-pass filter
                b0 = (1 + cosOmega) / 2;
                b1 = -(1 + cosOmega);
                b2 = (1 + cosOmega) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cosOmega;
                a2 = 1 - alpha;
                break;

            case EqBandMode.HighCut: // Low-pass filter
                b0 = (1 - cosOmega) / 2;
                b1 = 1 - cosOmega;
                b2 = (1 - cosOmega) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cosOmega;
                a2 = 1 - alpha;
                break;

            case EqBandMode.Notch:
                b0 = 1;
                b1 = -2 * cosOmega;
                b2 = 1;
                a0 = 1 + alpha;
                a1 = -2 * cosOmega;
                a2 = 1 - alpha;
                break;

            case EqBandMode.BandPass:
                b0 = alpha;
                b1 = 0;
                b2 = -alpha;
                a0 = 1 + alpha;
                a1 = -2 * cosOmega;
                a2 = 1 - alpha;
                break;

            default:
                return input;
        }

        // Normalize
        b0 /= a0; b1 /= a0; b2 /= a0;
        a1 /= a0; a2 /= a0;

        // Direct Form II transposed
        var state = band.State;
        double w = input - a1 * state[stateOffset] - a2 * state[stateOffset + 1];
        double output = b0 * w + b1 * state[stateOffset] + b2 * state[stateOffset + 1];

        state[stateOffset + 1] = state[stateOffset];
        state[stateOffset] = w;

        return output;
    }

    public override void Reset()
    {
        foreach (var band in Bands)
            band.ResetState();
    }
}
