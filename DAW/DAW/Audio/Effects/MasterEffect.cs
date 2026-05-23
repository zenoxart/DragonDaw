using System.Runtime.CompilerServices;

namespace DAW.Audio.Effects;

/// <summary>
/// Dragon Particle — God Particle-style Intelligent Mastering Chain
///
/// One knob (Amount) controls an entire mastering chain, pre-tuned like a
/// top engineer's session template. Under the hood, Amount scales all five
/// stages simultaneously using psychoacoustic curves:
///
///   1. Pre-EQ          — Tightens low end, clarifies mids, adds air to highs
///   2. Glue Compressor — Invisible dynamics control, program-dependent release
///   3. Harmonic Sat    — Even/odd order harmonics for warmth and perceived loudness
///   4. Stereo Enhancer — Frequency-dependent widening (highs wide, bass centered)
///   5. Limiter         — Transparent true-peak limiting for streaming loudness
///
/// User controls: Amount, InputGain, OutputGain, Low/Mid/High EQ trim
/// </summary>
public sealed class MasterEffect : AudioEffect
{
    public override string EffectType => "DragonParticle";
    public override string Icon       => "\U0001f409";

    // ── Volatile parameters (UI thread writes, audio thread reads) ────────────
    private volatile float _inputGain     = 1f;
    private volatile float _outputGain    = 1f;

    // Pre-EQ (Jaycen Joshua-style pre-balance)
    private volatile float _preEqSubCut   = 0f;
    private volatile float _preEqLowGain  = 0f;
    private volatile float _preEqLowMid   = 0f;
    private volatile float _preEqMid      = 0f;
    private volatile float _preEqHiMid    = 0f;
    private volatile float _preEqHigh     = 0f;

    // User EQ trim (±6 dB on top of pre-EQ)
    private volatile float _eqLow  = 0f;
    private volatile float _eqMid  = 0f;
    private volatile float _eqHigh = 0f;

    // Glue compressor
    private volatile float _compRatio   = 1.5f;
    private volatile float _compThresh  = -20f;
    private volatile float _compAttack  = 0.020f;
    private volatile float _compRelease = 0.150f;
    private volatile float _compKnee    = 6f;

    // Harmonic saturation
    private volatile float _satDrive = 0f;
    private volatile float _satEven  = 0.65f;
    private volatile float _satOdd   = 0.35f;

    // Stereo enhancer
    private volatile float _stereoWidth = 1f;

    // Limiter
    private volatile float _limThresh  = -0.5f;
    private volatile float _limRelease = 0.050f;

    // ── Setters ───────────────────────────────────────────────────────────────
    public void SetInputGain(float v)  => _inputGain  = v;
    public void SetOutputGain(float v) => _outputGain = v;

    public void SetPreEq(float subCut, float low, float lowMid,
                         float mid, float hiMid, float high)
    {
        _preEqSubCut  = subCut;
        _preEqLowGain = low;
        _preEqLowMid  = lowMid;
        _preEqMid     = mid;
        _preEqHiMid   = hiMid;
        _preEqHigh    = high;
    }

    public void SetUserEq(float lo, float mid, float hi)
    { _eqLow = lo; _eqMid = mid; _eqHigh = hi; }

    public void SetGlueComp(float ratio, float threshDb, float atk, float rel, float knee)
    {
        _compRatio   = ratio;
        _compThresh  = threshDb;
        _compAttack  = atk;
        _compRelease = rel;
        _compKnee    = knee;
    }

    public void SetSaturation(float drive, float even, float odd)
    { _satDrive = Math.Clamp(drive, 0, 1); _satEven = even; _satOdd = odd; }

    public void SetStereoWidth(float v) => _stereoWidth = Math.Clamp(v, 0.5f, 2f);

    public void SetLimiter(float threshDb, float rel)
    { _limThresh = Math.Clamp(threshDb, -12f, 0f); _limRelease = rel; }

    // ── Metering ──────────────────────────────────────────────────────────────
    private volatile float _meterRmsLinear  = 0f;
    private volatile float _meterPeakLinear = 0f;
    private volatile float _meterLufsPower  = 0f;

    public float MeterRmsLinear  => _meterRmsLinear;
    public float MeterPeakLinear => _meterPeakLinear;
    public float MeterLufsPower  => _meterLufsPower;

    // ── DSP state ─────────────────────────────────────────────────────────────
    private BqState _pHpL, _pHpR, _pLsL, _pLsR, _pLmL, _pLmR;
    private BqState _pMdL, _pMdR, _pHmL, _pHmR, _pHsL, _pHsR;
    private BqState _uLsL, _uLsR, _uMdL, _uMdR, _uHsL, _uHsR;
    private float   _compEnvL, _compEnvR;
    private float   _gainSmoothL = 1f, _gainSmoothR = 1f;
    private BqState _satHpL, _satHpR, _satDcL, _satDcR;
    private BqState _msHpL, _msHpR;
    private float   _limPeakL, _limPeakR;
    private BqState _kwHsL, _kwHsR, _kwHpL, _kwHpR;
    private float   _lufsIntegrated;
    private float   _rmsAccL, _rmsAccR, _peakHold;
    private int     _meterFrames;
    private const int MeterRate = 512;
    private int _lastSr;

    // ── Processing ────────────────────────────────────────────────────────────
    public override void ProcessSamples(float[] buffer, int offset, int count,
                                        int sampleRate, int channels)
    {
        if (!IsEnabled) return;
        if (sampleRate != _lastSr) { _lastSr = sampleRate; ResetState(); }

        float inG       = _inputGain;
        float outG      = _outputGain;
        float subCut    = _preEqSubCut;
        float preLoGain = _preEqLowGain;
        float preLmGain = _preEqLowMid;
        float preMdGain = _preEqMid;
        float preHmGain = _preEqHiMid;
        float preHiGain = _preEqHigh;
        float uLo       = _eqLow;
        float uMid      = _eqMid;
        float uHi       = _eqHigh;
        float cRatio    = _compRatio;
        float cThresh   = DbToLin(_compThresh);
        float cKnee     = _compKnee;
        float cAtkC     = MathF.Exp(-1f / (sampleRate * _compAttack));
        float cRelC     = MathF.Exp(-1f / (sampleRate * _compRelease));
        float sat       = _satDrive;
        float satEv     = _satEven;
        float satOd     = _satOdd;
        float width     = _stereoWidth;
        float limT      = DbToLin(_limThresh);
        float limRelC   = MathF.Exp(-1f / (sampleRate * _limRelease));

        bool     doHp   = subCut > 5f;
        BqCoeffs hpC    = doHp ? HP2(subCut, sampleRate) : default;
        BqCoeffs preLsC = LowShelf (preLoGain,  80f,    sampleRate);
        BqCoeffs preLmC = BellEq   (preLmGain,  200f,  0.9f, sampleRate);
        BqCoeffs preMdC = BellEq   (preMdGain, 2000f,  1.2f, sampleRate);
        BqCoeffs preHmC = BellEq   (preHmGain, 5000f,  0.8f, sampleRate);
        BqCoeffs preHsC = HighShelf(preHiGain, 12000f,       sampleRate);
        BqCoeffs uLsC   = LowShelf (uLo,  120f,   sampleRate);
        BqCoeffs uMdC   = BellEq   (uMid, 2500f, 0.8f, sampleRate);
        BqCoeffs uHsC   = HighShelf(uHi, 10000f,       sampleRate);
        BqCoeffs satHpC = HP2(80f,  sampleRate);
        BqCoeffs satDcC = HP2(5f,   sampleRate);
        BqCoeffs msHpC  = HP2(200f, sampleRate);
        BqCoeffs kwHsC  = KwHighShelf(sampleRate);
        BqCoeffs kwHpC  = KwHighPass(sampleRate);
        float lufsAlpha = MathF.Exp(-1f / (sampleRate * 0.4f));

        int frames = count / Math.Max(1, channels);

        for (int f = 0; f < frames; f++)
        {
            int idx = offset + f * channels;
            float xL = buffer[idx]     * inG;
            float xR = (channels > 1 ? buffer[idx + 1] : buffer[idx]) * inG;

            // Stage 1: Pre-EQ
            if (doHp) { xL = Bq(xL, ref _pHpL, hpC); xR = Bq(xR, ref _pHpR, hpC); }
            xL = Bq(xL, ref _pLsL, preLsC); xR = Bq(xR, ref _pLsR, preLsC);
            xL = Bq(xL, ref _pLmL, preLmC); xR = Bq(xR, ref _pLmR, preLmC);
            xL = Bq(xL, ref _pMdL, preMdC); xR = Bq(xR, ref _pMdR, preMdC);
            xL = Bq(xL, ref _pHmL, preHmC); xR = Bq(xR, ref _pHmR, preHmC);
            xL = Bq(xL, ref _pHsL, preHsC); xR = Bq(xR, ref _pHsR, preHsC);

            // User EQ
            xL = Bq(xL, ref _uLsL, uLsC); xR = Bq(xR, ref _uLsR, uLsC);
            xL = Bq(xL, ref _uMdL, uMdC); xR = Bq(xR, ref _uMdR, uMdC);
            xL = Bq(xL, ref _uHsL, uHsC); xR = Bq(xR, ref _uHsR, uHsC);

            // Stage 2: Glue compressor (M/S, soft-knee)
            float peak = MathF.Max(MathF.Abs((xL+xR)*0.5f), MathF.Abs((xL-xR)*0.5f));
            _compEnvL = peak > _compEnvL
                ? cAtkC * _compEnvL + (1-cAtkC) * peak
                : cRelC * _compEnvL + (1-cRelC) * peak;
            float gr = 1f;
            if (_compEnvL > 1e-8f)
            {
                float over = 20f * MathF.Log10(_compEnvL) - 20f * MathF.Log10(cThresh);
                if (over > -cKnee * 0.5f)
                {
                    float ko = over + cKnee * 0.5f;
                    float cd = ko < cKnee ? ko*ko/(2f*cKnee)*(1f-1f/cRatio) : over*(1f-1f/cRatio);
                    gr = DbToLin(-cd);
                }
            }
            _gainSmoothL += (gr - _gainSmoothL) * (1f - cRelC);
            xL *= _gainSmoothL; xR *= _gainSmoothL;

            // Stage 3: Parallel harmonic saturation
            if (sat > 0.001f)
            {
                float sL = Bq(xL, ref _satHpL, satHpC);
                float sR = Bq(xR, ref _satHpR, satHpC);
                float d = 1f + sat * 6f;
                float blend = sat * 0.40f;
                xL += (EvenHarm(sL*d)*satEv + OddHarm(sL*d)*satOd) * blend;
                xR += (EvenHarm(sR*d)*satEv + OddHarm(sR*d)*satOd) * blend;
                xL = Bq(xL, ref _satDcL, satDcC);
                xR = Bq(xR, ref _satDcR, satDcC);
            }

            // Stage 4: Frequency-dependent stereo widening
            if (MathF.Abs(width - 1f) > 0.005f)
            {
                float hfL = Bq(xL, ref _msHpL, msHpC), hfR = Bq(xR, ref _msHpR, msHpC);
                float lfMono = ((xL - hfL) + (xR - hfR)) * 0.5f;
                float hfM = (hfL + hfR) * 0.5f, hfS = (hfL - hfR) * 0.5f * width;
                xL = lfMono + hfM + hfS;
                xR = lfMono + hfM - hfS;
            }

            // Stage 5: True-peak limiter
            _limPeakL = MathF.Max(_limPeakL * limRelC, MathF.Abs(xL));
            _limPeakR = MathF.Max(_limPeakR * limRelC, MathF.Abs(xR));
            float pkMax = MathF.Max(_limPeakL, _limPeakR);
            if (pkMax > limT) { float lr = limT / pkMax; xL *= lr; xR *= lr; }

            xL *= outG; xR *= outG;
            buffer[idx] = xL;
            if (channels > 1) buffer[idx + 1] = xR;

            // Metering
            _rmsAccL += xL * xL; _rmsAccR += xR * xR;
            float pk2 = MathF.Max(MathF.Abs(xL), MathF.Abs(xR));
            if (pk2 > _peakHold) _peakHold = pk2;
            float kwL2 = Bq(Bq(xL, ref _kwHsL, kwHsC), ref _kwHpL, kwHpC);
            float kwR2 = Bq(Bq(xR, ref _kwHsR, kwHsC), ref _kwHpR, kwHpC);
            _lufsIntegrated = lufsAlpha * _lufsIntegrated
                            + (1f - lufsAlpha) * (kwL2*kwL2 + kwR2*kwR2);
            if (++_meterFrames >= MeterRate)
            {
                _meterRmsLinear  = MathF.Sqrt((_rmsAccL + _rmsAccR) / (2f * MeterRate));
                _meterPeakLinear = _peakHold;
                _meterLufsPower  = _lufsIntegrated;
                _peakHold       *= 0.998f;
                _rmsAccL = _rmsAccR = 0f;
                _meterFrames = 0;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EvenHarm(float x)
    {
        float shaped = x >= 0f ? TanhFast(x * 0.7f) : TanhFast(x * 1.5f) * 0.45f;
        return shaped - x * 0.48f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float OddHarm(float x) => TanhFast(x) - x * 0.88f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TanhFast(float x)
    { float x2 = x*x; return x*(27f+x2)/(27f+9f*x2); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DbToLin(float dB) => MathF.Pow(10f, dB / 20f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Bq(float x, ref BqState s, BqCoeffs c)
    {
        float y = c.B0*x+c.B1*s.X1+c.B2*s.X2-c.A1*s.Y1-c.A2*s.Y2;
        s.X2=s.X1; s.X1=x; s.Y2=s.Y1; s.Y1=y; return y;
    }

    private static BqCoeffs HP2(float freq, int sr)
    {
        double w0=2*Math.PI*freq/sr, cosW=Math.Cos(w0), sinW=Math.Sin(w0), a=sinW/(2*0.7071);
        return Norm((1+cosW)/2,-(1+cosW),(1+cosW)/2,1+a,-2*cosW,1-a);
    }

    private static BqCoeffs LowShelf(float g, float f, int sr)
    {
        double A=Math.Pow(10,g/40.0),sqA=Math.Sqrt(A),w0=2*Math.PI*f/sr,cw=Math.Cos(w0),sw=Math.Sin(w0);
        double al=sw/2*Math.Sqrt((A+1/A)*(1/1.0-1)+2);
        return Norm(A*((A+1)-(A-1)*cw+2*sqA*al),2*A*((A-1)-(A+1)*cw),A*((A+1)-(A-1)*cw-2*sqA*al),
                    (A+1)+(A-1)*cw+2*sqA*al,-2*((A-1)+(A+1)*cw),(A+1)+(A-1)*cw-2*sqA*al);
    }

    private static BqCoeffs HighShelf(float g, float f, int sr)
    {
        double A=Math.Pow(10,g/40.0),sqA=Math.Sqrt(A),w0=2*Math.PI*f/sr,cw=Math.Cos(w0),sw=Math.Sin(w0);
        double al=sw/2*Math.Sqrt((A+1/A)*(1/1.0-1)+2);
        return Norm(A*((A+1)+(A-1)*cw+2*sqA*al),-2*A*((A-1)+(A+1)*cw),A*((A+1)+(A-1)*cw-2*sqA*al),
                    (A+1)-(A-1)*cw+2*sqA*al,2*((A-1)-(A+1)*cw),(A+1)-(A-1)*cw-2*sqA*al);
    }

    private static BqCoeffs BellEq(float g, float f, float q, int sr)
    {
        double A=Math.Pow(10,g/40.0),w0=2*Math.PI*f/sr,al=Math.Sin(w0)/(2*q),cw=Math.Cos(w0);
        return Norm(1+al*A,-2*cw,1-al*A,1+al/A,-2*cw,1-al/A);
    }

    private static BqCoeffs KwHighShelf(int sr)
    {
        double Vh=Math.Pow(10.0,4.0/20.0),Vb=Math.Pow(Vh,0.4996667741545416);
        double w0=2*Math.PI*1681.974450955533/sr,cw=Math.Cos(w0),sw=Math.Sin(w0),al=sw/(2*0.7071752369554196);
        return Norm(Vh+Vb*al,-2*Vh*cw,Vh-Vb*al,1+al,-2*cw,1-al);
    }

    private static BqCoeffs KwHighPass(int sr)
    {
        double w0=2*Math.PI*38.13547087602444/sr,cw=Math.Cos(w0),sw=Math.Sin(w0),al=sw/(2*0.5003270373238773);
        return Norm((1+cw)/2,-(1+cw),(1+cw)/2,1+al,-2*cw,1-al);
    }

    private static BqCoeffs Norm(double b0,double b1,double b2,double a0,double a1,double a2)
        => new((float)(b0/a0),(float)(b1/a0),(float)(b2/a0),(float)(a1/a0),(float)(a2/a0));

    // Public filter primitives used by the DragonParticle DSP nodes
    public struct BqStatePublic { public float X1, X2, Y1, Y2; }
    public record struct BqCoeffsPublic(float B0, float B1, float B2, float A1, float A2);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BqPublic(float x, ref BqStatePublic s, BqCoeffsPublic c)
    { float y=c.B0*x+c.B1*s.X1+c.B2*s.X2-c.A1*s.Y1-c.A2*s.Y2; s.X2=s.X1;s.X1=x;s.Y2=s.Y1;s.Y1=y; return y; }

    public static BqCoeffsPublic LowShelfPublic(float g, float f, int sr)
    { var c=LowShelf(g,f,sr); return new(c.B0,c.B1,c.B2,c.A1,c.A2); }
    public static BqCoeffsPublic HighShelfPublic(float g, float f, int sr)
    { var c=HighShelf(g,f,sr); return new(c.B0,c.B1,c.B2,c.A1,c.A2); }
    public static BqCoeffsPublic BellPublic(float g, float f, float q, int sr)
    { var c=BellEq(g,f,q,sr); return new(c.B0,c.B1,c.B2,c.A1,c.A2); }
    public static BqCoeffsPublic HP2Public(float f, int sr)
    { var c=HP2(f,sr); return new(c.B0,c.B1,c.B2,c.A1,c.A2); }

    private void ResetState()
    {
        _pHpL=_pHpR=_pLsL=_pLsR=_pLmL=_pLmR=_pMdL=_pMdR=_pHmL=_pHmR=_pHsL=_pHsR=default;
        _uLsL=_uLsR=_uMdL=_uMdR=_uHsL=_uHsR=default;
        _compEnvL=_compEnvR=0f; _gainSmoothL=_gainSmoothR=1f;
        _satHpL=_satHpR=_satDcL=_satDcR=_msHpL=_msHpR=default;
        _limPeakL=_limPeakR=0f;
        _kwHsL=_kwHsR=_kwHpL=_kwHpR=default;
        _lufsIntegrated=_rmsAccL=_rmsAccR=_peakHold=0f;
    }

    public override void Reset() => ResetState();

    private struct BqCoeffs
    {
        public float B0,B1,B2,A1,A2;
        public BqCoeffs(float b0,float b1,float b2,float a1,float a2)
        { B0=b0;B1=b1;B2=b2;A1=a1;A2=a2; }
    }
    private struct BqState { public float X1,X2,Y1,Y2; }
}
