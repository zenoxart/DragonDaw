using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// High-quality algorithmic reverb inspired by Valhalla Room.
/// 
/// Signal flow:
///   Input → Pre-delay → Early Reflections → (EarlySend) → Late Reverb FDN → Mix
///                                                                  ↑
///                                              frequency-dependent feedback
///
/// Stage 1 – Early Reflections:
///   Multi-tap delay network → nested allpass diffusers
///   Parameters: EarlySize, EarlyDiffusion, EarlyCross, EarlyModRate/Depth
///
/// Stage 2 – Late Reverberation (4×4 Feedback Delay Network):
///   4 modulated delay lines with Hadamard mixing matrix
///   Frequency-dependent feedback: bass multiplier + high-cut + shelving
///   Parameters: LateSize, LateCross, Decay, BassMult, BassXover,
///               HighCut, HighShelf, LowShelf
///
/// Global: Mix, PreDelay, Depth (early/late balance), ReverbMode
/// </summary>
public class ReverbEffect : AudioEffect
{
    // ── Global parameters ──────────────────────────────────────────────────
    private double _mix = 0.35;
    private double _preDelay = 20.0;
    private double _depth = 0.5;
    private int _reverbMode;

    // ── Early Reflection parameters ────────────────────────────────────────
    private double _earlySize = 0.5;
    private double _earlyDiffusion = 0.7;
    private double _earlyCross = 0.3;
    private double _earlySend = 0.6;
    private double _earlyModRate = 0.8;
    private double _earlyModDepth = 0.3;

    // ── Late Reverberation parameters ──────────────────────────────────────
    private double _lateSize = 0.5;
    private double _lateCross = 0.3;
    private double _decay = 2.0;
    private double _bassMult = 1.0;
    private double _bassXover = 200.0;
    private double _highCut = 8000.0;
    private double _highShelf = -3.0;
    private double _lowShelf;

    // ── Legacy compatibility ───────────────────────────────────────────────
    private double _roomSize = 0.5;
    private double _damping = 0.5;
    private double _wetLevel = 0.35;

    // ── DSP state ──────────────────────────────────────────────────────────
    private bool _initialized;
    private int _lastSampleRate;

    // Pre-delay
    private float[] _preDelayBufL = [];
    private float[] _preDelayBufR = [];
    private int _preDelayIdx;

    // Early reflections: multi-tap delays (6 taps per channel)
    private const int EarlyTapCount = 6;
    private float[] _earlyBufL = [];
    private float[] _earlyBufR = [];
    private int _earlyWriteIdx;
    private int _earlyBufSize;
    private readonly int[] _earlyTapOffsetsL = new int[EarlyTapCount];
    private readonly int[] _earlyTapOffsetsR = new int[EarlyTapCount];
    private readonly float[] _earlyTapGains = new float[EarlyTapCount];

    // Early diffusers: 2 nested allpass filters per channel
    private const int EarlyDiffuserCount = 2;
    private readonly float[][] _earlyDiffBufL = new float[EarlyDiffuserCount][];
    private readonly float[][] _earlyDiffBufR = new float[EarlyDiffuserCount][];
    private readonly int[] _earlyDiffIdx = new int[EarlyDiffuserCount];
    private static readonly int[] EarlyDiffSizes = [142, 107];

    // Early modulation LFO
    private double _earlyLfoPhase;

    // Late FDN: 4 delay lines
    private const int FdnSize = 4;
    private readonly float[][] _fdnBufL = new float[FdnSize][];
    private readonly float[][] _fdnBufR = new float[FdnSize][];
    private readonly int[] _fdnWriteIdx = new int[FdnSize];
    private readonly int[] _fdnDelaySamples = new int[FdnSize];

    // Base delay lengths at 44100 Hz (mutually prime for density)
    private static readonly int[] FdnBaseDelays = [1427, 1777, 2137, 2473];

    // FDN modulation LFOs
    private readonly double[] _fdnLfoPhase = new double[FdnSize];
    private static readonly double[] FdnLfoRates = [0.73, 0.97, 0.63, 0.83];

    // Frequency-dependent feedback filters (per delay line, per channel)
    private readonly float[] _fdnLpStateL = new float[FdnSize];
    private readonly float[] _fdnLpStateR = new float[FdnSize];
    private readonly float[] _fdnHpStateL = new float[FdnSize];
    private readonly float[] _fdnHpStateR = new float[FdnSize];
    private readonly float[] _fdnHsStateL = new float[FdnSize];
    private readonly float[] _fdnHsStateR = new float[FdnSize];
    private readonly float[] _fdnLsStateL = new float[FdnSize];
    private readonly float[] _fdnLsStateR = new float[FdnSize];

    // Mode presets: [earlyDiffusion, density factor, modulation scale]
    private static readonly double[][] ModePresets =
    [
        [0.70, 1.00, 0.30],  // 0: Natural Room
        [0.85, 0.80, 0.15],  // 1: Small Chamber
        [0.50, 1.20, 0.50],  // 2: Large Hall
        [0.90, 0.60, 0.80],  // 3: Plate
        [0.30, 1.50, 0.10],  // 4: Cathedral
    ];

    public static readonly string[] ModeNames = ["Natural Room", "Small Chamber", "Large Hall", "Plate", "Cathedral"];

    public ReverbEffect()
    {
        Name = "Reverb";
    }

    public override string EffectType => "Reverb";
    public override string Icon => "🏛️";

    // ── Global properties ──────────────────────────────────────────────────

    /// <summary>Dry/Wet mix (0–1)</summary>
    public double Mix
    {
        get => _mix;
        set { if (SetField(ref _mix, Math.Clamp(value, 0, 1))) SyncLegacy(); }
    }

    /// <summary>Pre-delay in ms (0–200)</summary>
    public double PreDelay
    {
        get => _preDelay;
        set => SetField(ref _preDelay, Math.Clamp(value, 0, 200));
    }

    /// <summary>Early/Late balance: 0 = all early, 1 = all late</summary>
    public double Depth
    {
        get => _depth;
        set => SetField(ref _depth, Math.Clamp(value, 0, 1));
    }

    /// <summary>Algorithm mode index (0–4)</summary>
    public int ReverbMode
    {
        get => _reverbMode;
        set => SetField(ref _reverbMode, Math.Clamp(value, 0, ModePresets.Length - 1));
    }

    // ── Early Reflection properties ────────────────────────────────────────

    /// <summary>Early impulse length (0–1, maps to 10–120ms)</summary>
    public double EarlySize
    {
        get => _earlySize;
        set { if (SetField(ref _earlySize, Math.Clamp(value, 0, 1))) _initialized = false; }
    }

    /// <summary>Echo density / diffusion amount (0–1)</summary>
    public double EarlyDiffusion
    {
        get => _earlyDiffusion;
        set => SetField(ref _earlyDiffusion, Math.Clamp(value, 0, 1));
    }

    /// <summary>Stereo crossfeed between L/R early reflections (0–1)</summary>
    public double EarlyCross
    {
        get => _earlyCross;
        set => SetField(ref _earlyCross, Math.Clamp(value, 0, 1));
    }

    /// <summary>Amount of early reflections sent into the late reverb (0–1)</summary>
    public double EarlySend
    {
        get => _earlySend;
        set => SetField(ref _earlySend, Math.Clamp(value, 0, 1));
    }

    /// <summary>Early modulation rate in Hz (0.1–5.0)</summary>
    public double EarlyModRate
    {
        get => _earlyModRate;
        set => SetField(ref _earlyModRate, Math.Clamp(value, 0.1, 5.0));
    }

    /// <summary>Early modulation depth (0–1, maps to 0–4 samples)</summary>
    public double EarlyModDepth
    {
        get => _earlyModDepth;
        set => SetField(ref _earlyModDepth, Math.Clamp(value, 0, 1));
    }

    // ── Late Reverberation properties ──────────────────────────────────────

    /// <summary>Virtual room size scaling (0–1)</summary>
    public double LateSize
    {
        get => _lateSize;
        set { if (SetField(ref _lateSize, Math.Clamp(value, 0, 1))) { _initialized = false; SyncLegacy(); } }
    }

    /// <summary>Stereo coupling in feedback network (0–1)</summary>
    public double LateCross
    {
        get => _lateCross;
        set => SetField(ref _lateCross, Math.Clamp(value, 0, 1));
    }

    /// <summary>RT60 decay time in seconds (0.1–30)</summary>
    public double Decay
    {
        get => _decay;
        set => SetField(ref _decay, Math.Clamp(value, 0.1, 30.0));
    }

    /// <summary>Low frequency decay multiplier (0.5–2.0)</summary>
    public double BassMult
    {
        get => _bassMult;
        set => SetField(ref _bassMult, Math.Clamp(value, 0.5, 2.0));
    }

    /// <summary>Bass crossover frequency in Hz (50–500)</summary>
    public double BassXover
    {
        get => _bassXover;
        set => SetField(ref _bassXover, Math.Clamp(value, 50, 500));
    }

    /// <summary>High frequency cutoff in Hz (1000–20000)</summary>
    public double HighCut
    {
        get => _highCut;
        set { if (SetField(ref _highCut, Math.Clamp(value, 1000, 20000))) SyncLegacy(); }
    }

    /// <summary>High shelf gain in dB (-12 to 0)</summary>
    public double HighShelf
    {
        get => _highShelf;
        set => SetField(ref _highShelf, Math.Clamp(value, -12, 0));
    }

    /// <summary>Low shelf gain in dB (-6 to +6)</summary>
    public double LowShelf
    {
        get => _lowShelf;
        set => SetField(ref _lowShelf, Math.Clamp(value, -6, 6));
    }

    // ── Legacy properties (backward compat with old projects) ──────────────

    /// <summary>Room size (0–1) — maps to LateSize + EarlySize</summary>
    public double RoomSize
    {
        get => _roomSize;
        set
        {
            if (SetField(ref _roomSize, Math.Clamp(value, 0, 1)))
            {
                _lateSize = _roomSize;
                _earlySize = _roomSize;
                _initialized = false;
            }
        }
    }

    /// <summary>Damping (0–1) — maps to HighCut</summary>
    public double Damping
    {
        get => _damping;
        set
        {
            if (SetField(ref _damping, Math.Clamp(value, 0, 1)))
                _highCut = 1000 + (1 - _damping) * 19000;
        }
    }

    /// <summary>Wet level (0–1) — maps to Mix</summary>
    public double WetLevel
    {
        get => _wetLevel;
        set
        {
            if (SetField(ref _wetLevel, Math.Clamp(value, 0, 1)))
                _mix = _wetLevel;
        }
    }

    private void SyncLegacy()
    {
        _roomSize = _lateSize;
        _wetLevel = _mix;
        _damping = Math.Clamp(1.0 - (_highCut - 1000) / 19000.0, 0, 1);
    }

    // ── Initialization ─────────────────────────────────────────────────────

    private void Initialize(int sampleRate)
    {
        if (_initialized && _lastSampleRate == sampleRate) return;

        double scale = sampleRate / 44100.0;

        // Pre-delay buffer (max 200ms)
        int maxPreDelay = (int)(0.2 * sampleRate) + 1;
        _preDelayBufL = new float[maxPreDelay];
        _preDelayBufR = new float[maxPreDelay];
        _preDelayIdx = 0;

        // ── Early reflections ──
        double earlySizeMs = 10 + _earlySize * 110;
        int earlySizeSamples = (int)(earlySizeMs * 0.001 * sampleRate);
        _earlyBufSize = earlySizeSamples + 64;
        _earlyBufL = new float[_earlyBufSize];
        _earlyBufR = new float[_earlyBufSize];
        _earlyWriteIdx = 0;

        // Fibonacci-ratio tap spacing for natural density
        double[] tapRatios = [0.08, 0.17, 0.31, 0.48, 0.67, 1.00];
        for (int i = 0; i < EarlyTapCount; i++)
        {
            int baseTap = (int)(tapRatios[i] * earlySizeSamples);
            _earlyTapOffsetsL[i] = Math.Max(1, baseTap);
            _earlyTapOffsetsR[i] = Math.Max(1, baseTap + (int)(3.7 * (i + 1) * scale));
            _earlyTapGains[i] = 1.0f / (1.0f + 0.5f * i);
        }

        // Normalize tap gains (energy-preserving)
        float totalGain = 0;
        for (int i = 0; i < EarlyTapCount; i++) totalGain += _earlyTapGains[i] * _earlyTapGains[i];
        totalGain = MathF.Sqrt(totalGain);
        if (totalGain > 0)
            for (int i = 0; i < EarlyTapCount; i++) _earlyTapGains[i] /= totalGain;

        // Early diffusers
        for (int d = 0; d < EarlyDiffuserCount; d++)
        {
            int sz = (int)(EarlyDiffSizes[d] * scale);
            _earlyDiffBufL[d] = new float[Math.Max(1, sz)];
            _earlyDiffBufR[d] = new float[Math.Max(1, sz)];
            _earlyDiffIdx[d] = 0;
        }

        // ── Late FDN ──
        double sizeScale = 0.5 + _lateSize * 1.5;
        for (int i = 0; i < FdnSize; i++)
        {
            int delaySamples = (int)(FdnBaseDelays[i] * scale * sizeScale);
            _fdnDelaySamples[i] = Math.Max(4, delaySamples);
            int bufSize = delaySamples + 32;
            _fdnBufL[i] = new float[Math.Max(4, bufSize)];
            _fdnBufR[i] = new float[Math.Max(4, bufSize)];
            _fdnWriteIdx[i] = 0;
        }

        Array.Clear(_fdnLpStateL); Array.Clear(_fdnLpStateR);
        Array.Clear(_fdnHpStateL); Array.Clear(_fdnHpStateR);
        Array.Clear(_fdnHsStateL); Array.Clear(_fdnHsStateR);
        Array.Clear(_fdnLsStateL); Array.Clear(_fdnLsStateR);
        _earlyLfoPhase = 0;
        Array.Clear(_fdnLfoPhase);

        _initialized = true;
        _lastSampleRate = sampleRate;
    }

    // ── Processing ─────────────────────────────────────────────────────────

    public override void ProcessSamples(float[] buffer, int offset, int count, int sampleRate, int channels)
    {
        if (!IsEnabled) return;

        Initialize(sampleRate);

        // Cache parameters
        float mix = (float)_mix;
        float dry = 1f - mix;
        float earlyGain = 1f - (float)_depth;
        float lateGain = (float)_depth;
        float earlySendAmt = (float)_earlySend;
        float earlyCross = (float)_earlyCross;
        float lateCross = (float)_lateCross;
        float earlyDiff = (float)_earlyDiffusion;

        // Mode modifiers
        var mode = ModePresets[Math.Clamp(_reverbMode, 0, ModePresets.Length - 1)];
        float modeDiffusion = (float)mode[0];
        float modeModScale = (float)mode[2];
        float effectiveDiff = earlyDiff * modeDiffusion;

        // Feedback from RT60
        float[] feedbackGains = new float[FdnSize];
        for (int i = 0; i < FdnSize; i++)
            feedbackGains[i] = (float)Math.Pow(0.001, (double)_fdnDelaySamples[i] / (sampleRate * _decay));

        // Filter coefficients
        float lpCoeff = (float)(1.0 - Math.Exp(-2.0 * Math.PI * _highCut / sampleRate));
        float hpCoeff = (float)(1.0 - Math.Exp(-2.0 * Math.PI * _bassXover / sampleRate));
        float bassMultF = (float)_bassMult;
        float highShelfGain = (float)Math.Pow(10, _highShelf / 20.0);
        float lowShelfGain = (float)Math.Pow(10, _lowShelf / 20.0);
        float highShelfCoeff = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 4000.0 / sampleRate));
        float lowShelfCoeff = (float)(1.0 - Math.Exp(-2.0 * Math.PI * 300.0 / sampleRate));

        // LFO
        double earlyLfoInc = 2.0 * Math.PI * _earlyModRate / sampleRate;
        float earlyModDepthSamples = (float)(_earlyModDepth * 4.0 * modeModScale);
        double[] fdnLfoInc = new double[FdnSize];
        for (int i = 0; i < FdnSize; i++)
            fdnLfoInc[i] = 2.0 * Math.PI * FdnLfoRates[i] * modeModScale / sampleRate;
        float fdnModDepth = 3.0f * modeModScale;

        int preDelayMax = _preDelayBufL.Length;
        int preDelaySmp = Math.Clamp((int)(_preDelay * 0.001 * sampleRate), 0, preDelayMax - 1);
        int step = channels > 1 ? 2 : 1;
        int endIndex = offset + count;

        for (int i = offset; i < endIndex; i += step)
        {
            float inL = buffer[i];
            float inR = channels > 1 ? buffer[i + 1] : inL;

            // ── Pre-delay ──
            _preDelayBufL[_preDelayIdx] = inL;
            _preDelayBufR[_preDelayIdx] = inR;
            int rdIdx = (_preDelayIdx - preDelaySmp + preDelayMax) % preDelayMax;
            float pdL = _preDelayBufL[rdIdx];
            float pdR = _preDelayBufR[rdIdx];
            _preDelayIdx = (_preDelayIdx + 1) % preDelayMax;

            // ── Early Reflections ──
            int ewIdx = _earlyWriteIdx % _earlyBufSize;
            _earlyBufL[ewIdx] = pdL;
            _earlyBufR[ewIdx] = pdR;

            float earlyLfo = (float)Math.Sin(_earlyLfoPhase) * earlyModDepthSamples;
            _earlyLfoPhase += earlyLfoInc;
            if (_earlyLfoPhase > 2.0 * Math.PI) _earlyLfoPhase -= 2.0 * Math.PI;

            float erL = 0, erR = 0;
            for (int t = 0; t < EarlyTapCount; t++)
            {
                float modOff = earlyLfo * (t % 2 == 0 ? 1f : -0.7f);
                int tapL = Math.Clamp((int)(_earlyTapOffsetsL[t] + modOff), 1, _earlyBufSize - 1);
                int tapR = Math.Clamp((int)(_earlyTapOffsetsR[t] - modOff * 0.6f), 1, _earlyBufSize - 1);

                erL += _earlyBufL[(_earlyWriteIdx - tapL + _earlyBufSize) % _earlyBufSize] * _earlyTapGains[t];
                erR += _earlyBufR[(_earlyWriteIdx - tapR + _earlyBufSize) % _earlyBufSize] * _earlyTapGains[t];
            }
            _earlyWriteIdx = (_earlyWriteIdx + 1) % _earlyBufSize;

            // Stereo crossfeed
            float crossNorm = 1f / (1f + earlyCross);
            float erLx = (erL + erR * earlyCross) * crossNorm;
            float erRx = (erR + erL * earlyCross) * crossNorm;
            erL = erLx;
            erR = erRx;

            // Diffusion (nested allpass)
            for (int d = 0; d < EarlyDiffuserCount; d++)
            {
                var bufDL = _earlyDiffBufL[d];
                var bufDR = _earlyDiffBufR[d];
                if (bufDL == null || bufDR == null || bufDL.Length == 0) continue;

                int dIdx = _earlyDiffIdx[d] % bufDL.Length;
                float dOutL = bufDL[dIdx];
                float dOutR = bufDR[dIdx];

                bufDL[dIdx] = erL + dOutL * effectiveDiff;
                bufDR[dIdx] = erR + dOutR * effectiveDiff;
                erL = dOutL - erL * effectiveDiff;
                erR = dOutR - erR * effectiveDiff;

                _earlyDiffIdx[d] = (dIdx + 1) % bufDL.Length;
            }

            // ── Late FDN ──
            float fdnInL = pdL * (1f - earlySendAmt) + erL * earlySendAmt;
            float fdnInR = pdR * (1f - earlySendAmt) + erR * earlySendAmt;

            // Read from delay lines with interpolated modulation
            float fo0L = 0, fo1L = 0, fo2L = 0, fo3L = 0;
            float fo0R = 0, fo1R = 0, fo2R = 0, fo3R = 0;

            for (int f = 0; f < FdnSize; f++)
            {
                var bL = _fdnBufL[f];
                var bR = _fdnBufR[f];
                if (bL == null || bR == null || bL.Length < 4) continue;

                float lfo = (float)Math.Sin(_fdnLfoPhase[f]) * fdnModDepth;
                _fdnLfoPhase[f] += fdnLfoInc[f];
                if (_fdnLfoPhase[f] > 2.0 * Math.PI) _fdnLfoPhase[f] -= 2.0 * Math.PI;

                float readPos = _fdnDelaySamples[f] + lfo;
                int readInt = Math.Clamp((int)readPos, 1, bL.Length - 2);
                float frac = readPos - readInt;

                int r0 = (_fdnWriteIdx[f] - readInt + bL.Length) % bL.Length;
                int r1 = (r0 - 1 + bL.Length) % bL.Length;

                float oL = bL[r0] * (1f - frac) + bL[r1] * frac;
                float oR = bR[r0] * (1f - frac) + bR[r1] * frac;

                switch (f)
                {
                    case 0: fo0L = oL; fo0R = oR; break;
                    case 1: fo1L = oL; fo1R = oR; break;
                    case 2: fo2L = oL; fo2R = oR; break;
                    case 3: fo3L = oL; fo3R = oR; break;
                }
            }

            // Hadamard mixing (energy preserving)
            const float hh = 0.5f;
            float m0L = hh * (fo0L + fo1L + fo2L + fo3L);
            float m1L = hh * (fo0L - fo1L + fo2L - fo3L);
            float m2L = hh * (fo0L + fo1L - fo2L - fo3L);
            float m3L = hh * (fo0L - fo1L - fo2L + fo3L);
            float m0R = hh * (fo0R + fo1R + fo2R + fo3R);
            float m1R = hh * (fo0R - fo1R + fo2R - fo3R);
            float m2R = hh * (fo0R + fo1R - fo2R - fo3R);
            float m3R = hh * (fo0R - fo1R - fo2R + fo3R);

            // Cross-coupling, filtering, and write-back
            float lateOutL = 0, lateOutR = 0;
            ProcessFdnLine(0, m0L, m0R, fdnInL, fdnInR, ref lateOutL, ref lateOutR, fo0L, fo0R,
                feedbackGains, lpCoeff, hpCoeff, bassMultF, highShelfGain, lowShelfGain,
                highShelfCoeff, lowShelfCoeff, lateCross);
            ProcessFdnLine(1, m1L, m1R, fdnInL, fdnInR, ref lateOutL, ref lateOutR, fo1L, fo1R,
                feedbackGains, lpCoeff, hpCoeff, bassMultF, highShelfGain, lowShelfGain,
                highShelfCoeff, lowShelfCoeff, lateCross);
            ProcessFdnLine(2, m2L, m2R, fdnInL, fdnInR, ref lateOutL, ref lateOutR, fo2L, fo2R,
                feedbackGains, lpCoeff, hpCoeff, bassMultF, highShelfGain, lowShelfGain,
                highShelfCoeff, lowShelfCoeff, lateCross);
            ProcessFdnLine(3, m3L, m3R, fdnInL, fdnInR, ref lateOutL, ref lateOutR, fo3L, fo3R,
                feedbackGains, lpCoeff, hpCoeff, bassMultF, highShelfGain, lowShelfGain,
                highShelfCoeff, lowShelfCoeff, lateCross);

            lateOutL *= 0.5f;
            lateOutR *= 0.5f;

            // ── Final mix ──
            // Use pre-delayed signal for dry path so dry and wet are time-aligned
            float wetL = erL * earlyGain + lateOutL * lateGain;
            float wetR = erR * earlyGain + lateOutR * lateGain;

            buffer[i] = Math.Clamp(pdL * dry + wetL * mix, -1f, 1f);
            if (channels > 1)
                buffer[i + 1] = Math.Clamp(pdR * dry + wetR * mix, -1f, 1f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessFdnLine(int f, float mL, float mR, float fdnInL, float fdnInR,
        ref float lateOutL, ref float lateOutR, float rawOutL, float rawOutR,
        float[] feedbackGains, float lpCoeff, float hpCoeff, float bassMultF,
        float highShelfGain, float lowShelfGain, float highShelfCoeff, float lowShelfCoeff,
        float lateCross)
    {
        // Cross-coupling
        float cn = 1f / (1f + lateCross);
        float fbL = (mL + mR * lateCross) * cn * feedbackGains[f];
        float fbR = (mR + mL * lateCross) * cn * feedbackGains[f];

        // High-cut
        _fdnLpStateL[f] += lpCoeff * (fbL - _fdnLpStateL[f]);
        _fdnLpStateR[f] += lpCoeff * (fbR - _fdnLpStateR[f]);
        fbL = _fdnLpStateL[f];
        fbR = _fdnLpStateR[f];

        // Bass multiplier
        float hpL = fbL - _fdnHpStateL[f];
        float hpR = fbR - _fdnHpStateR[f];
        _fdnHpStateL[f] += hpCoeff * hpL;
        _fdnHpStateR[f] += hpCoeff * hpR;
        fbL = hpL + _fdnHpStateL[f] * bassMultF;
        fbR = hpR + _fdnHpStateR[f] * bassMultF;

        // High shelf
        _fdnHsStateL[f] += highShelfCoeff * (fbL - _fdnHsStateL[f]);
        _fdnHsStateR[f] += highShelfCoeff * (fbR - _fdnHsStateR[f]);
        fbL += (_fdnHsStateL[f] - fbL) * (1f - highShelfGain);
        fbR += (_fdnHsStateR[f] - fbR) * (1f - highShelfGain);

        // Low shelf
        _fdnLsStateL[f] += lowShelfCoeff * (fbL - _fdnLsStateL[f]);
        _fdnLsStateR[f] += lowShelfCoeff * (fbR - _fdnLsStateR[f]);
        fbL += (_fdnLsStateL[f] - fbL) * (1f - lowShelfGain);
        fbR += (_fdnLsStateR[f] - fbR) * (1f - lowShelfGain);

        // Soft clip + write
        fbL = SoftClip(fbL);
        fbR = SoftClip(fbR);

        var bL = _fdnBufL[f];
        var bR = _fdnBufR[f];
        if (bL is { Length: > 0 } && bR is { Length: > 0 })
        {
            bL[_fdnWriteIdx[f]] = fdnInL * 0.25f + fbL;
            bR[_fdnWriteIdx[f]] = fdnInR * 0.25f + fbR;
            _fdnWriteIdx[f] = (_fdnWriteIdx[f] + 1) % bL.Length;
        }

        lateOutL += rawOutL;
        lateOutR += rawOutR;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftClip(float x)
    {
        if (x > 1f) return 1f - 1f / (1f + x);
        if (x < -1f) return -1f + 1f / (1f - x);
        return x;
    }

    public override void Reset()
    {
        if (_preDelayBufL.Length > 0) Array.Clear(_preDelayBufL);
        if (_preDelayBufR.Length > 0) Array.Clear(_preDelayBufR);
        if (_earlyBufL.Length > 0) Array.Clear(_earlyBufL);
        if (_earlyBufR.Length > 0) Array.Clear(_earlyBufR);

        for (int d = 0; d < EarlyDiffuserCount; d++)
        {
            if (_earlyDiffBufL[d] != null) Array.Clear(_earlyDiffBufL[d]);
            if (_earlyDiffBufR[d] != null) Array.Clear(_earlyDiffBufR[d]);
        }

        for (int f = 0; f < FdnSize; f++)
        {
            if (_fdnBufL[f] != null) Array.Clear(_fdnBufL[f]);
            if (_fdnBufR[f] != null) Array.Clear(_fdnBufR[f]);
        }

        Array.Clear(_fdnLpStateL); Array.Clear(_fdnLpStateR);
        Array.Clear(_fdnHpStateL); Array.Clear(_fdnHpStateR);
        Array.Clear(_fdnHsStateL); Array.Clear(_fdnHsStateR);
        Array.Clear(_fdnLsStateL); Array.Clear(_fdnLsStateR);

        _initialized = false;
    }
}
