using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects.DragonParticle;

// ═══════════════════════════════════════════════════════════════════════════════
//  MODEL LAYER — Pure DSP, zero UI awareness
//
//  Parallel DAG topology:
//
//   UpperRange (input split)
//       │
//       ├── Chain A: MV2 → PuigChild670 → Scheps73      latency: 256+128+64 = 448
//       │
//       ├── Chain B: API550B                              latency: 256
//       │
//       └── Chain C: NLSBuss                             latency: 128
//
//   All chains padded with delay-lines to 448 samples before MagicMids sums them.
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Readonly snapshot of a node's latency for the Presenter → View.</summary>
public record NodeLatencyInfo(string NodeId, string Label, int SelfLatency, int TotalLatency);

// ── Shared DSP helpers ────────────────────────────────────────────────────────

internal static class Dsp
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float TanhFast(float x)
    { float x2 = x * x; return x * (27f + x2) / (27f + 9f * x2); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float DbToLin(float db) => MathF.Pow(10f, db / 20f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float SoftClip(float x, float drive)
    { float s = x * drive; return s / (1f + MathF.Abs(s)); }
}

// ── Stereo delay line (latency compensation) ──────────────────────────────────

internal sealed class StereoDelayLine
{
    private float[] _bufL, _bufR;
    private int _head, _len;

    public int DelaySamples { get; private set; }

    public StereoDelayLine(int maxSamples)
    {
        _len = Math.Max(maxSamples + 1, 2);
        _bufL = new float[_len];
        _bufR = new float[_len];
    }

    public void SetDelay(int samples)
    {
        DelaySamples = samples;
        if (samples + 1 > _len)
        {
            _len = samples + 1;
            _bufL = new float[_len];
            _bufR = new float[_len];
            _head = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public (float L, float R) Process(float inL, float inR)
    {
        if (DelaySamples == 0) return (inL, inR);
        int readHead = (_head - DelaySamples + _len) % _len;
        float outL = _bufL[readHead], outR = _bufR[readHead];
        _bufL[_head] = inL; _bufR[_head] = inR;
        _head = (_head + 1) % _len;
        return (outL, outR);
    }

    public void Reset() { Array.Clear(_bufL); Array.Clear(_bufR); _head = 0; }
}

// ═══════════════════════════════════════════════════════════════════════════════
//  DSP NODES
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// MV2 — Dynamic range processor (Waves MV2 style).
/// Upward compression of quiet passages + downward compression of peaks.
/// Self-latency: 256 samples (lookahead peak detection).
/// </summary>
internal sealed class Mv2Node
{
    public const int SelfLatency = 256;
    public int TotalLatency { get; set; }

    private readonly StereoDelayLine _lookahead = new(SelfLatency);
    private float _envLowL, _envLowR, _envHighL, _envHighR;

    public float LowRatio   { get; set; } = 2.0f;
    public float HighRatio  { get; set; } = 4.0f;
    public float LowThresh  { get; set; } = Dsp.DbToLin(-30f);
    public float HighThresh { get; set; } = Dsp.DbToLin(-12f);
    public float Attack     { get; set; } = 0.003f;
    public float Release    { get; set; } = 0.200f;

    public (float L, float R) Process(float xL, float xR, int sr)
    {
        float atkC = MathF.Exp(-1f / (sr * Attack));
        float relC = MathF.Exp(-1f / (sr * Release));

        var (dL, dR) = _lookahead.Process(xL, xR);

        float absL = MathF.Abs(xL), absR = MathF.Abs(xR);

        // Upward: bring up quiet material
        _envLowL = absL < _envLowL ? relC*_envLowL+(1-relC)*absL : atkC*_envLowL+(1-atkC)*absL;
        _envLowR = absR < _envLowR ? relC*_envLowR+(1-relC)*absR : atkC*_envLowR+(1-atkC)*absR;

        float gainL = 1f, gainR = 1f;
        if (_envLowL > 1e-8f && _envLowL < LowThresh)
            gainL = MathF.Pow(LowThresh / _envLowL, 1f / LowRatio - 1f);
        if (_envLowR > 1e-8f && _envLowR < LowThresh)
            gainR = MathF.Pow(LowThresh / _envLowR, 1f / LowRatio - 1f);

        // Downward: control loud peaks
        _envHighL = absL > _envHighL ? atkC*_envHighL+(1-atkC)*absL : relC*_envHighL+(1-relC)*absL;
        _envHighR = absR > _envHighR ? atkC*_envHighR+(1-atkC)*absR : relC*_envHighR+(1-relC)*absR;

        if (_envHighL > HighThresh)
        { float gr = HighThresh+(_envHighL-HighThresh)/HighRatio; gainL *= gr/MathF.Max(_envHighL,1e-10f); }
        if (_envHighR > HighThresh)
        { float gr = HighThresh+(_envHighR-HighThresh)/HighRatio; gainR *= gr/MathF.Max(_envHighR,1e-10f); }

        return (dL * gainL, dR * gainR);
    }

    public void Reset() { _lookahead.Reset(); _envLowL=_envLowR=_envHighL=_envHighR=0f; }
}

/// <summary>
/// PuigChild 670 — Variable-mu tube compressor emulation.
/// Program-dependent time constants + triode even-order harmonics.
/// Self-latency: 128 samples.
/// </summary>
internal sealed class PuigChild670Node
{
    public const int SelfLatency = 128;
    public int TotalLatency { get; set; }

    private readonly StereoDelayLine _delay = new(SelfLatency);
    private float _envL, _envR;

    public float Threshold { get; set; } = Dsp.DbToLin(-18f);
    public float Ratio     { get; set; } = 3.0f;
    public float Drive     { get; set; } = 0.3f;

    public (float L, float R) Process(float xL, float xR, int sr)
    {
        float level = (MathF.Abs(xL) + MathF.Abs(xR)) * 0.5f;
        float tc    = 0.050f + (1f - Math.Clamp(level / 0.5f, 0f, 1f)) * 0.150f;
        float atkC  = MathF.Exp(-1f / (sr * tc * 0.5f));
        float relC  = MathF.Exp(-1f / (sr * tc));

        var (dL, dR) = _delay.Process(xL, xR);

        float absL = MathF.Abs(xL), absR = MathF.Abs(xR);
        _envL = absL > _envL ? atkC*_envL+(1-atkC)*absL : relC*_envL+(1-relC)*absL;
        _envR = absR > _envR ? atkC*_envR+(1-atkC)*absR : relC*_envR+(1-relC)*absR;

        float gainL = 1f, gainR = 1f;
        if (_envL > Threshold) { float gr = Threshold+(_envL-Threshold)/Ratio; gainL = gr/MathF.Max(_envL,1e-10f); }
        if (_envR > Threshold) { float gr = Threshold+(_envR-Threshold)/Ratio; gainR = gr/MathF.Max(_envR,1e-10f); }

        float yL = dL * gainL, yR = dR * gainR;

        // Tube triode even-order colouration
        if (Drive > 0.001f)
        {
            yL += (Dsp.SoftClip(yL, 1f + Drive*3f) - yL * 0.5f) * Drive * 0.35f;
            yR += (Dsp.SoftClip(yR, 1f + Drive*3f) - yR * 0.5f) * Drive * 0.35f;
        }

        return (yL, yR);
    }

    public void Reset() { _delay.Reset(); _envL = _envR = 0f; }
}

/// <summary>
/// Scheps 73 — Neve 1073-style EQ + transformer saturation.
/// Low shelf warmth, high shelf air, transformer iron colouration.
/// Self-latency: 64 samples.
/// </summary>
internal sealed class Scheps73Node
{
    public const int SelfLatency = 64;
    public int TotalLatency { get; set; }

    private readonly StereoDelayLine _delay = new(SelfLatency);
    private MasterEffect.BqStatePublic _lsL,_lsR,_hsL,_hsR,_hpL,_hpR;

    public float LowShelfGain  { get; set; } = 0f;
    public float HighShelfGain { get; set; } = 0f;
    public float HpfFreq       { get; set; } = 30f;
    public float TransDrive    { get; set; } = 0.15f;

    public (float L, float R) Process(float xL, float xR, int sr)
    {
        var (dL, dR) = _delay.Process(xL, xR);

        var lsC = MasterEffect.LowShelfPublic(LowShelfGain,   80f,   sr);
        var hsC = MasterEffect.HighShelfPublic(HighShelfGain, 12000f, sr);
        var hpC = MasterEffect.HP2Public(HpfFreq, sr);

        float yL = MasterEffect.BqPublic(dL, ref _lsL, lsC);
        float yR = MasterEffect.BqPublic(dR, ref _lsR, lsC);
        yL = MasterEffect.BqPublic(yL, ref _hsL, hsC);
        yR = MasterEffect.BqPublic(yR, ref _hsR, hsC);
        yL = MasterEffect.BqPublic(yL, ref _hpL, hpC);
        yR = MasterEffect.BqPublic(yR, ref _hpR, hpC);

        if (TransDrive > 0.001f)
        {
            yL += (Dsp.SoftClip(yL * (1f+TransDrive), 1f) - yL * 0.5f) * TransDrive * 0.3f;
            yR += (Dsp.SoftClip(yR * (1f+TransDrive), 1f) - yR * 0.5f) * TransDrive * 0.3f;
        }

        return (yL, yR);
    }

    public void Reset() { _delay.Reset(); _lsL=_lsR=_hsL=_hsR=_hpL=_hpR=default; }
}

/// <summary>
/// API 550B — 5-band proportional-Q EQ.
/// Q narrows as gain increases for surgical, musical correction.
/// Self-latency: 256 samples.
/// </summary>
internal sealed class Api550BNode
{
    public const int SelfLatency = 256;
    public int TotalLatency { get; set; }

    private readonly StereoDelayLine _delay = new(SelfLatency);
    private MasterEffect.BqStatePublic _b1L,_b1R,_b2L,_b2R,_b3L,_b3R,_b4L,_b4R,_b5L,_b5R;

    public float Band1Gain { get; set; } = 0f;  // LS  @50 Hz
    public float Band2Gain { get; set; } = 0f;  // Bell @200 Hz
    public float Band3Gain { get; set; } = 0f;  // Bell @1.5 kHz
    public float Band4Gain { get; set; } = 0f;  // Bell @5 kHz
    public float Band5Gain { get; set; } = 0f;  // HS  @10 kHz

    private static float PropQ(float g) => 0.4f + MathF.Abs(g) * 0.15f;

    public (float L, float R) Process(float xL, float xR, int sr)
    {
        var (dL, dR) = _delay.Process(xL, xR);

        var c1 = MasterEffect.LowShelfPublic(Band1Gain, 50f,   sr);
        var c2 = MasterEffect.BellPublic(Band2Gain,  200f,  PropQ(Band2Gain), sr);
        var c3 = MasterEffect.BellPublic(Band3Gain, 1500f,  PropQ(Band3Gain), sr);
        var c4 = MasterEffect.BellPublic(Band4Gain, 5000f,  PropQ(Band4Gain), sr);
        var c5 = MasterEffect.HighShelfPublic(Band5Gain, 10000f, sr);

        float yL = dL, yR = dR;
        yL = MasterEffect.BqPublic(yL, ref _b1L, c1); yR = MasterEffect.BqPublic(yR, ref _b1R, c1);
        yL = MasterEffect.BqPublic(yL, ref _b2L, c2); yR = MasterEffect.BqPublic(yR, ref _b2R, c2);
        yL = MasterEffect.BqPublic(yL, ref _b3L, c3); yR = MasterEffect.BqPublic(yR, ref _b3R, c3);
        yL = MasterEffect.BqPublic(yL, ref _b4L, c4); yR = MasterEffect.BqPublic(yR, ref _b4R, c4);
        yL = MasterEffect.BqPublic(yL, ref _b5L, c5); yR = MasterEffect.BqPublic(yR, ref _b5R, c5);

        return (yL, yR);
    }

    public void Reset() { _delay.Reset(); _b1L=_b1R=_b2L=_b2R=_b3L=_b3R=_b4L=_b4R=_b5L=_b5R=default; }
}

/// <summary>
/// NLS Buss — Neve console analogue saturation.
/// Parallel harmonic injection: only the distortion products are added back,
/// so the fundamental stays clean while harmonics accumulate.
/// Drive range 0.05–0.55 set by Presenter; audible saturation starts ~0.15.
/// Self-latency: 128 samples.
/// </summary>
internal sealed class NlsBussNode
{
    public const int SelfLatency = 128;
    public int TotalLatency { get; set; }

    private readonly StereoDelayLine _delay = new(SelfLatency);
    private MasterEffect.BqStatePublic _dcL, _dcR;
    // HP pre-filter so sub-bass doesn't fold into harmonics
    private MasterEffect.BqStatePublic _hpL, _hpR;

    public float Drive { get; set; } = 0.2f;

    public (float L, float R) Process(float xL, float xR, int sr)
    {
        var (dL, dR) = _delay.Process(xL, xR);

        // HP at 90 Hz — keeps sub-bass out of harmonic generation path
        var hpC = MasterEffect.HP2Public(90f, sr);
        float sL = MasterEffect.BqPublic(dL, ref _hpL, hpC);
        float sR = MasterEffect.BqPublic(dR, ref _hpR, hpC);

        // Scale into the saturation zone — more drive = more harmonics
        // Drive=0.05 -> d=1.3x, Drive=0.55 -> d=6.5x
        float d = 1f + Drive * 10f;
        float sLd = sL * d;
        float sRd = sR * d;

        // Even-order (2nd harmonic, Neve warmth): asymmetric soft-clip
        // SoftClip(x) - x*0.5 leaves predominantly 2nd harmonic residual
        float e2L = (Dsp.SoftClip(sLd, 1.3f) - sLd * 0.5f) * Drive;
        float e2R = (Dsp.SoftClip(sRd, 1.3f) - sRd * 0.5f) * Drive;

        // Odd-order (3rd harmonic, Neve bite): tanh residual
        // tanh(x) - x*0.88 ~ -x^3/3 (3rd harmonic dominant)
        float o3L = (Dsp.TanhFast(sLd) - sLd * 0.88f) * Drive;
        float o3R = (Dsp.TanhFast(sRd) - sRd * 0.88f) * Drive;

        // Neve flavour: slightly more odd than even for that gritty console tone
        float harmL = e2L * 0.45f + o3L * 0.55f;
        float harmR = e2R * 0.45f + o3R * 0.55f;

        float yL = dL + harmL;
        float yR = dR + harmR;

        // DC block (HP @5 Hz) — removes any bias from asymmetric saturation
        var dcC = MasterEffect.HP2Public(5f, sr);
        yL = MasterEffect.BqPublic(yL, ref _dcL, dcC);
        yR = MasterEffect.BqPublic(yR, ref _dcR, dcC);

        return (yL, yR);
    }

    public void Reset() { _delay.Reset(); _dcL = _dcR = _hpL = _hpR = default; }
}

/// <summary>
/// MagicMids — latency-compensated parallel summing node.
/// Uses unity-gain normalisation (weights / sum) so the three chains blend
/// at full level — no RMS suppression that would fight against saturation.
/// An optional post-saturation stage adds one final layer of analogue colour
/// to the summed bus.
/// </summary>
internal sealed class MagicMidsNode
{
    public const int SelfLatency = 0;
    public int TotalLatency { get; set; }

    public float ChainAWeight { get; set; } = 0.40f;
    public float ChainBWeight { get; set; } = 0.35f;
    public float ChainCWeight { get; set; } = 0.25f;

    /// <summary>
    /// Post-sum saturation applied to the mixed bus.
    /// 0 = bypass, 1 = full console glue.  Driven by Amount via Presenter.
    /// </summary>
    public float BusDrive { get; set; } = 0f;

    // Bus saturation state
    private MasterEffect.BqStatePublic _busHpL, _busHpR;

    public (float L, float R) Sum(
        float aL, float aR,
        float bL, float bR,
        float cL, float cR,
        int sr = 44100)
    {
        // Unity-gain weighted sum: weights / (wA+wB+wC)
        // This preserves full signal level so saturation has maximum headroom to work with.
        float total = ChainAWeight + ChainBWeight + ChainCWeight;
        float inv   = total > 1e-6f ? 1f / total : 1f;

        float yL = (aL * ChainAWeight + bL * ChainBWeight + cL * ChainCWeight) * inv;
        float yR = (aR * ChainAWeight + bR * ChainBWeight + cR * ChainCWeight) * inv;

        // Post-sum bus saturation — console glue on the summed signal
        if (BusDrive > 0.001f)
        {
            // HP at 90 Hz protects sub-bass
            var hpC = MasterEffect.HP2Public(90f, sr);
            float sL = MasterEffect.BqPublic(yL, ref _busHpL, hpC);
            float sR = MasterEffect.BqPublic(yR, ref _busHpR, hpC);

            float d = 1f + BusDrive * 8f;

            // Parallel harmonic injection: even + odd, add only the harmonics
            float e2L = (Dsp.SoftClip(sL * d, 1.4f) - sL * d * 0.5f) * BusDrive * 0.5f;
            float e2R = (Dsp.SoftClip(sR * d, 1.4f) - sR * d * 0.5f) * BusDrive * 0.5f;
            float o3L = (Dsp.TanhFast(sL * d) - sL * d * 0.88f)       * BusDrive * 0.5f;
            float o3R = (Dsp.TanhFast(sR * d) - sR * d * 0.88f)       * BusDrive * 0.5f;

            yL += e2L * 0.4f + o3L * 0.6f;
            yR += e2R * 0.4f + o3R * 0.6f;
        }

        return (yL, yR);
    }

    public void Reset() { _busHpL = _busHpR = default; }
}
