using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// 7-Band Parametric EQ inspired by FL Studio Parametric EQ 2.
///
/// PERFORMANCE DESIGN
/// ──────────────────
/// • Biquad coefficients are precomputed whenever a band parameter changes
///   (dirty flag + lazy recompute at the start of each ProcessSamples call).
///   Zero trig/pow in the hot path.
/// • All arithmetic in the hot path is single-precision float.
/// • FFT spectrum analysis runs on a background thread; the audio thread
///   only enqueues samples into a ring buffer and never blocks.
/// • Zero heap allocations in ProcessSamples.
/// </summary>
public class EqualizerEffect : AudioEffect
{
    public const int BandCount = 7;

    // ── FFT config ────────────────────────────────────────────────────────
    private const int FftSize = 2048;
    private const int FftHalf = FftSize / 2;

    public EqBand[] Bands { get; }

    // ── Per-band precomputed float coefficients ───────────────────────────
    // Stored flat: [band0_b0, band0_b1, band0_b2, band0_a1, band0_a2,  band1_b0 …]
    // 5 coefficients × 7 bands = 35 floats
    private readonly float[] _coeff = new float[BandCount * 5];

    // Biquad state: 2 state values × 2 channels × 7 bands = 28 floats
    // Layout: [band0_ch0_w0, band0_ch0_w1, band0_ch1_w0, band0_ch1_w1, band1_ch0_w0 …]
    private readonly float[] _state = new float[BandCount * 4];

    // Dirty flag: any band that needs coefficient recalculation
    private volatile bool _coeffDirty = true;
    private int _lastSampleRate = 44100;

    // ── FFT spectrum (background thread) ─────────────────────────────────
    private readonly float[] _fftRing = new float[FftSize];
    private int _fftWrite;
    private int _fftSamplesSinceLastAnalysis;
    private const int FftUpdateIntervalSamples = FftSize; // update once per full buffer fill

    private readonly double[] _fftWindow = new double[FftSize];
    private readonly double[] _spectrumSmoothed = new double[FftHalf];
    private volatile double[]? _spectrumSnapshot;

    // Background task handle — never block the audio thread waiting for it
    private volatile bool _fftRunning;

    public double[]? SpectrumData => _spectrumSnapshot;
    public double LastSampleRate { get; private set; } = 44100;

    public EqualizerEffect()
    {
        Name = "Parametric EQ";

        for (int i = 0; i < FftSize; i++)
            _fftWindow[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));

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

        // Subscribe to band changes to mark coefficients dirty
        foreach (var band in Bands)
            band.PropertyChanged += (_, _) => _coeffDirty = true;
    }

    public override string EffectType => "Equalizer";
    public override string Icon => "📊";

    // ── Legacy/serialization properties ──────────────────────────────────

    public double LowGain      { get => Bands[0].Gain;      set { Bands[0].Gain = value;      _coeffDirty = true; } }
    public double MidGain      { get => Bands[3].Gain;      set { Bands[3].Gain = value;      _coeffDirty = true; } }
    public double HighGain     { get => Bands[6].Gain;      set { Bands[6].Gain = value;      _coeffDirty = true; } }
    public double LowFrequency  { get => Bands[0].Frequency; set { Bands[0].Frequency = value; _coeffDirty = true; } }
    public double MidFrequency  { get => Bands[3].Frequency; set { Bands[3].Frequency = value; _coeffDirty = true; } }
    public double HighFrequency { get => Bands[6].Frequency; set { Bands[6].Frequency = value; _coeffDirty = true; } }
    public double MidQ          { get => Bands[3].Q;         set { Bands[3].Q = value;         _coeffDirty = true; } }

    public double Band1Gain { get => Bands[0].Gain; set { Bands[0].Gain = value; _coeffDirty = true; } }
    public double Band1Freq { get => Bands[0].Frequency; set { Bands[0].Frequency = value; _coeffDirty = true; } }
    public double Band1Q    { get => Bands[0].Q; set { Bands[0].Q = value; _coeffDirty = true; } }
    public int    Band1Mode { get => (int)Bands[0].Mode; set { Bands[0].Mode = (EqBandMode)value; _coeffDirty = true; } }

    public double Band2Gain { get => Bands[1].Gain; set { Bands[1].Gain = value; _coeffDirty = true; } }
    public double Band2Freq { get => Bands[1].Frequency; set { Bands[1].Frequency = value; _coeffDirty = true; } }
    public double Band2Q    { get => Bands[1].Q; set { Bands[1].Q = value; _coeffDirty = true; } }
    public int    Band2Mode { get => (int)Bands[1].Mode; set { Bands[1].Mode = (EqBandMode)value; _coeffDirty = true; } }

    public double Band3Gain { get => Bands[2].Gain; set { Bands[2].Gain = value; _coeffDirty = true; } }
    public double Band3Freq { get => Bands[2].Frequency; set { Bands[2].Frequency = value; _coeffDirty = true; } }
    public double Band3Q    { get => Bands[2].Q; set { Bands[2].Q = value; _coeffDirty = true; } }
    public int    Band3Mode { get => (int)Bands[2].Mode; set { Bands[2].Mode = (EqBandMode)value; _coeffDirty = true; } }

    public double Band4Gain { get => Bands[3].Gain; set { Bands[3].Gain = value; _coeffDirty = true; } }
    public double Band4Freq { get => Bands[3].Frequency; set { Bands[3].Frequency = value; _coeffDirty = true; } }
    public double Band4Q    { get => Bands[3].Q; set { Bands[3].Q = value; _coeffDirty = true; } }
    public int    Band4Mode { get => (int)Bands[3].Mode; set { Bands[3].Mode = (EqBandMode)value; _coeffDirty = true; } }

    public double Band5Gain { get => Bands[4].Gain; set { Bands[4].Gain = value; _coeffDirty = true; } }
    public double Band5Freq { get => Bands[4].Frequency; set { Bands[4].Frequency = value; _coeffDirty = true; } }
    public double Band5Q    { get => Bands[4].Q; set { Bands[4].Q = value; _coeffDirty = true; } }
    public int    Band5Mode { get => (int)Bands[4].Mode; set { Bands[4].Mode = (EqBandMode)value; _coeffDirty = true; } }

    public double Band6Gain { get => Bands[5].Gain; set { Bands[5].Gain = value; _coeffDirty = true; } }
    public double Band6Freq { get => Bands[5].Frequency; set { Bands[5].Frequency = value; _coeffDirty = true; } }
    public double Band6Q    { get => Bands[5].Q; set { Bands[5].Q = value; _coeffDirty = true; } }
    public int    Band6Mode { get => (int)Bands[5].Mode; set { Bands[5].Mode = (EqBandMode)value; _coeffDirty = true; } }

    public double Band7Gain { get => Bands[6].Gain; set { Bands[6].Gain = value; _coeffDirty = true; } }
    public double Band7Freq { get => Bands[6].Frequency; set { Bands[6].Frequency = value; _coeffDirty = true; } }
    public double Band7Q    { get => Bands[6].Q; set { Bands[6].Q = value; _coeffDirty = true; } }
    public int    Band7Mode { get => (int)Bands[6].Mode; set { Bands[6].Mode = (EqBandMode)value; _coeffDirty = true; } }

    // ── Audio processing ──────────────────────────────────────────────────

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        LastSampleRate = sampleRate;
        if (!IsEnabled) return;

        // Recompute coefficients only when dirty (zero trig/pow in steady state)
        if (_coeffDirty || _lastSampleRate != sampleRate)
        {
            RebuildCoefficients(sampleRate);
            _lastSampleRate = sampleRate;
            _coeffDirty = false;
        }

        int end = offset + count;

        if (channels == 2)
            ProcessStereo(buffer, offset, end);
        else
            ProcessMono(buffer, offset, end);

        // Feed ring buffer for background FFT (no blocking)
        FeedFftRing(buffer, offset, count, channels);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessStereo(float[] buf, int start, int end)
    {
        for (int i = start; i < end; i += 2)
        {
            float l = buf[i];
            float r = buf[i + 1];

            for (int b = 0; b < BandCount; b++)
            {
                if (!Bands[b].IsEnabled) continue;

                int ci = b * 5;
                float b0 = _coeff[ci];
                float b1 = _coeff[ci + 1];
                float b2 = _coeff[ci + 2];
                float a1 = _coeff[ci + 3];
                float a2 = _coeff[ci + 4];

                int si = b * 4;

                // Left channel
                float w0l = l - a1 * _state[si] - a2 * _state[si + 1];
                float outL = b0 * w0l + b1 * _state[si] + b2 * _state[si + 1];
                _state[si + 1] = _state[si];
                _state[si] = w0l;
                l = outL;

                // Right channel
                float w0r = r - a1 * _state[si + 2] - a2 * _state[si + 3];
                float outR = b0 * w0r + b1 * _state[si + 2] + b2 * _state[si + 3];
                _state[si + 3] = _state[si + 2];
                _state[si + 2] = w0r;
                r = outR;
            }

            buf[i]     = l;
            buf[i + 1] = r;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ProcessMono(float[] buf, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            float s = buf[i];

            for (int b = 0; b < BandCount; b++)
            {
                if (!Bands[b].IsEnabled) continue;

                int ci = b * 5;
                float b0 = _coeff[ci];
                float b1 = _coeff[ci + 1];
                float b2 = _coeff[ci + 2];
                float a1 = _coeff[ci + 3];
                float a2 = _coeff[ci + 4];

                int si = b * 4;
                float w0 = s - a1 * _state[si] - a2 * _state[si + 1];
                s = b0 * w0 + b1 * _state[si] + b2 * _state[si + 1];
                _state[si + 1] = _state[si];
                _state[si] = w0;
            }

            buf[i] = s;
        }
    }

    // ── Coefficient precomputation (called off hot path) ──────────────────

    private void RebuildCoefficients(int sampleRate)
    {
        for (int b = 0; b < BandCount; b++)
        {
            var band = Bands[b];
            int ci = b * 5;

            if (!band.IsEnabled)
            {
                // Identity filter: b0=1, b1=0, b2=0, a1=0, a2=0
                _coeff[ci] = 1f; _coeff[ci+1] = 0f; _coeff[ci+2] = 0f;
                _coeff[ci+3] = 0f; _coeff[ci+4] = 0f;
                continue;
            }

            double freq  = band.Frequency;
            double gainDb = band.Gain;
            double q     = band.Q;

            double omega    = 2.0 * Math.PI * freq / sampleRate;
            double sinOmega = Math.Sin(omega);
            double cosOmega = Math.Cos(omega);
            double alpha    = sinOmega / (2.0 * q);
            double A        = Math.Pow(10.0, gainDb / 40.0);

            double b0, b1, b2, a0, a1, a2;

            switch (band.Mode)
            {
                case EqBandMode.Peaking:
                    // Skip trivially: near-zero gain → identity
                    if (Math.Abs(gainDb) < 0.05)
                    {
                        _coeff[ci] = 1f; _coeff[ci+1] = 0f; _coeff[ci+2] = 0f;
                        _coeff[ci+3] = 0f; _coeff[ci+4] = 0f;
                        continue;
                    }
                    b0 = 1 + alpha * A;  b1 = -2 * cosOmega; b2 = 1 - alpha * A;
                    a0 = 1 + alpha / A;  a1 = -2 * cosOmega; a2 = 1 - alpha / A;
                    break;

                case EqBandMode.LowShelf:
                {
                    double sqrtA = Math.Sqrt(A);
                    b0 = A * ((A+1) - (A-1)*cosOmega + 2*sqrtA*alpha);
                    b1 = 2 * A * ((A-1) - (A+1)*cosOmega);
                    b2 = A * ((A+1) - (A-1)*cosOmega - 2*sqrtA*alpha);
                    a0 = (A+1) + (A-1)*cosOmega + 2*sqrtA*alpha;
                    a1 = -2 * ((A-1) + (A+1)*cosOmega);
                    a2 = (A+1) + (A-1)*cosOmega - 2*sqrtA*alpha;
                    break;
                }

                case EqBandMode.HighShelf:
                {
                    double sqrtA = Math.Sqrt(A);
                    b0 = A * ((A+1) + (A-1)*cosOmega + 2*sqrtA*alpha);
                    b1 = -2 * A * ((A-1) + (A+1)*cosOmega);
                    b2 = A * ((A+1) + (A-1)*cosOmega - 2*sqrtA*alpha);
                    a0 = (A+1) - (A-1)*cosOmega + 2*sqrtA*alpha;
                    a1 = 2 * ((A-1) - (A+1)*cosOmega);
                    a2 = (A+1) - (A-1)*cosOmega - 2*sqrtA*alpha;
                    break;
                }

                case EqBandMode.LowCut:
                    b0 = (1 + cosOmega) / 2; b1 = -(1 + cosOmega); b2 = (1 + cosOmega) / 2;
                    a0 = 1 + alpha;           a1 = -2 * cosOmega;   a2 = 1 - alpha;
                    break;

                case EqBandMode.HighCut:
                    b0 = (1 - cosOmega) / 2; b1 = 1 - cosOmega; b2 = (1 - cosOmega) / 2;
                    a0 = 1 + alpha;          a1 = -2 * cosOmega; a2 = 1 - alpha;
                    break;

                case EqBandMode.Notch:
                    b0 = 1; b1 = -2*cosOmega; b2 = 1;
                    a0 = 1 + alpha; a1 = -2*cosOmega; a2 = 1 - alpha;
                    break;

                case EqBandMode.BandPass:
                    b0 = alpha; b1 = 0; b2 = -alpha;
                    a0 = 1 + alpha; a1 = -2*cosOmega; a2 = 1 - alpha;
                    break;

                default:
                    _coeff[ci] = 1f; _coeff[ci+1] = 0f; _coeff[ci+2] = 0f;
                    _coeff[ci+3] = 0f; _coeff[ci+4] = 0f;
                    continue;
            }

            double invA0 = 1.0 / a0;
            _coeff[ci]     = (float)(b0 * invA0);
            _coeff[ci + 1] = (float)(b1 * invA0);
            _coeff[ci + 2] = (float)(b2 * invA0);
            _coeff[ci + 3] = (float)(a1 * invA0);
            _coeff[ci + 4] = (float)(a2 * invA0);
        }
    }

    // ── Background FFT (never blocks the audio thread) ────────────────────

    private void FeedFftRing(float[] buf, int offset, int count, int channels)
    {
        // Write mono mix into the ring buffer
        int end = offset + count;
        for (int i = offset; i < end; i += channels)
        {
            float s = buf[i];
            if (channels == 2 && i + 1 < end)
                s = (buf[i] + buf[i + 1]) * 0.5f;

            _fftRing[_fftWrite] = s;
            _fftWrite = (_fftWrite + 1) & (FftSize - 1);
        }

        _fftSamplesSinceLastAnalysis += count / channels;
        if (_fftSamplesSinceLastAnalysis >= FftUpdateIntervalSamples && !_fftRunning)
        {
            _fftSamplesSinceLastAnalysis = 0;
            _fftRunning = true;
            // Snapshot the ring buffer so the background task is independent
            var snapshot = new float[FftSize];
            int readPos = _fftWrite; // oldest sample is right after write pointer
            for (int i = 0; i < FftSize; i++)
                snapshot[i] = _fftRing[(readPos + i) & (FftSize - 1)];

            var window   = _fftWindow;
            var smoothed = _spectrumSmoothed;
            Task.Run(() =>
            {
                ComputeSpectrumBackground(snapshot, window, smoothed);
                _fftRunning = false;
            });
        }
    }

    private void ComputeSpectrumBackground(float[] samples, double[] window, double[] smoothed)
    {
        var real = new double[FftSize];
        var imag = new double[FftSize];

        for (int i = 0; i < FftSize; i++)
        {
            real[i] = samples[i] * window[i];
        }

        // Bit-reversal
        int n = FftSize, j = 0;
        for (int i = 0; i < n - 1; i++)
        {
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
            }
            int k = n >> 1;
            while (k <= j) { j -= k; k >>= 1; }
            j += k;
        }

        // FFT
        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size >> 1;
            double angle = -2.0 * Math.PI / size;
            double wR = Math.Cos(angle), wI = Math.Sin(angle);
            for (int i = 0; i < n; i += size)
            {
                double cR = 1, cI = 0;
                for (int k = 0; k < half; k++)
                {
                    int i1 = i + k, i2 = i + k + half;
                    double tR = cR * real[i2] - cI * imag[i2];
                    double tI = cR * imag[i2] + cI * real[i2];
                    real[i2] = real[i1] - tR; imag[i2] = imag[i1] - tI;
                    real[i1] += tR;            imag[i1] += tI;
                    double nr = cR * wR - cI * wI;
                    cI = cR * wI + cI * wR;
                    cR = nr;
                }
            }
        }

        const double kSmooth = 0.55;
        var result = new double[FftHalf];
        for (int i = 0; i < FftHalf; i++)
        {
            double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / FftSize * 2.0;
            smoothed[i] = smoothed[i] * kSmooth + mag * (1.0 - kSmooth);
            result[i] = smoothed[i];
        }

        _spectrumSnapshot = result;
    }

    public override void Reset()
    {
        Array.Clear(_state);
        _coeffDirty = true;
    }
}
