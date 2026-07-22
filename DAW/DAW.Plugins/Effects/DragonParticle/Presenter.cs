namespace DAW.Audio.Effects.DragonParticle;

public sealed class DragonParticlePresenter
{
    // ── Nodes ─────────────────────────────────────────────────────────────────
    private readonly Mv2Node          _mv2        = new();
    private readonly PuigChild670Node _puigChild  = new();
    private readonly Scheps73Node     _scheps73   = new();
    private readonly Api550BNode      _api550B    = new();
    private readonly NlsBussNode      _nlsBuss    = new();
    private readonly MagicMidsNode    _magicMids  = new();

    private readonly StereoDelayLine _compBDelay = new(512);
    private readonly StereoDelayLine _compCDelay = new(512);

    // Chain A: MV2(256) + PuigChild(128) + Scheps73(64) = 448
    // Chain B: API550B(256)                              = 256  -> pad +192
    // Chain C: NLSBuss(128)                              = 128  -> pad +320
    private const int ChainALatency = Mv2Node.SelfLatency + PuigChild670Node.SelfLatency + Scheps73Node.SelfLatency;
    private const int ChainBLatency = Api550BNode.SelfLatency;
    private const int ChainCLatency = NlsBussNode.SelfLatency;
    private const int MaxLatency    = ChainALatency; // 448

    private volatile float _amount = 0.3f;

    public IReadOnlyList<NodeLatencyInfo> NodeLatencies { get; }

    public DragonParticlePresenter()
    {
        _mv2.TotalLatency       = Mv2Node.SelfLatency;
        _puigChild.TotalLatency = _mv2.TotalLatency + PuigChild670Node.SelfLatency;
        _scheps73.TotalLatency  = _puigChild.TotalLatency + Scheps73Node.SelfLatency;
        _api550B.TotalLatency   = Api550BNode.SelfLatency;
        _nlsBuss.TotalLatency   = NlsBussNode.SelfLatency;
        _magicMids.TotalLatency = MaxLatency;

        _compBDelay.SetDelay(MaxLatency - ChainBLatency);  // 192 samples
        _compCDelay.SetDelay(MaxLatency - ChainCLatency);  // 320 samples

        NodeLatencies = BuildLatencySnapshot();
        ApplyAmount(_amount);
    }

    // ── Amount mapping ────────────────────────────────────────────────────────

    public void SetAmount(float amount)
    {
        _amount = Math.Clamp(amount, 0f, 1f);
        ApplyAmount(_amount);
    }

    private void ApplyAmount(float a)
    {
        float knee(float x, float k)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            float t = (x - k) / (1 - k);
            if (t <= 0) return 0;
            return t * t * (3 - 2 * t);
        }

        // MV2
        _mv2.LowThresh  = Dsp.DbToLin(-40f + knee(a, 0.1f) * 15f);
        _mv2.HighThresh = Dsp.DbToLin(-8f  - knee(a, 0.3f) * 10f);
        _mv2.HighRatio  = 1.5f + knee(a, 0.3f) * 4.5f;

        // PuigChild
        _puigChild.Threshold = Dsp.DbToLin(-16f - knee(a, 0.4f) * 10f);
        _puigChild.Ratio     = 2f + knee(a, 0.4f) * 4f;
        _puigChild.Drive     = knee(a, 0.2f) * 0.45f;

        // Scheps73
        _scheps73.LowShelfGain  = knee(a, 0.3f) * 2.5f;
        _scheps73.HighShelfGain = knee(a, 0.2f) * 1.8f;
        _scheps73.TransDrive    = 0.08f + knee(a, 0.3f) * 0.22f;

        // API-550B
        _api550B.Band1Gain = knee(a, 0.5f) * 1.5f;
        _api550B.Band3Gain = knee(a, 0.3f) * 2.0f;
        _api550B.Band4Gain = knee(a, 0.2f) * 1.5f;

        // NLS Buss — now has meaningful drive range thanks to reworked waveshaper
        // 0.05 at Amount=0 (almost inaudible), 0.55 at Amount=1 (clearly saturated)
        _nlsBuss.Drive = 0.05f + knee(a, 0.25f) * 0.50f;

        // MagicMids chain weights
        // Amount = 0.0  ->  Clean:   API-550B dominant (EQ colour, no saturation)
        // Amount = 0.5  ->  Balanced: equal blend
        // Amount = 1.0  ->  Heavy:   NLS Buss + tube chain dominant
        float chainA, chainB, chainC;
        if (a <= 0.5f)
        {
            float t = a * 2f;
            chainA = 0.20f + t * 0.15f;  // 0.20 -> 0.35
            chainB = 0.60f - t * 0.25f;  // 0.60 -> 0.35
            chainC = 0.20f + t * 0.10f;  // 0.20 -> 0.30
        }
        else
        {
            float t = (a - 0.5f) * 2f;
            chainA = 0.35f + t * 0.10f;  // 0.35 -> 0.45
            chainB = 0.35f - t * 0.20f;  // 0.35 -> 0.15
            chainC = 0.30f + t * 0.10f;  // 0.30 -> 0.40
        }
        _magicMids.ChainAWeight = chainA;
        _magicMids.ChainBWeight = chainB;
        _magicMids.ChainCWeight = chainC;

        // MagicMids bus drive — post-sum saturation that glues everything together.
        // Starts only after Amount > 0.25 so it doesn't colour clean settings.
        // At Amount = 1.0 it reaches 0.60 which is clearly audible saturation.
        _magicMids.BusDrive = Math.Max(0f, (a - 0.25f) / 0.75f) * 0.60f;
    }

    // ── Audio processing ──────────────────────────────────────────────────────

    public (float L, float R) ProcessFrame(float xL, float xR, int sr)
    {
        // Chain A: MV2 -> PuigChild -> Scheps73
        var (aL, aR) = _mv2.Process(xL, xR, sr);
        (aL, aR)     = _puigChild.Process(aL, aR, sr);
        (aL, aR)     = _scheps73.Process(aL, aR, sr);

        // Chain B: API550B + latency pad
        var (bL, bR) = _api550B.Process(xL, xR, sr);
        (bL, bR)     = _compBDelay.Process(bL, bR);

        // Chain C: NLSBuss + latency pad
        var (cL, cR) = _nlsBuss.Process(xL, xR, sr);
        (cL, cR)     = _compCDelay.Process(cL, cR);

        // Latency-compensated sum with post-bus saturation
        return _magicMids.Sum(aL, aR, bL, bR, cL, cR, sr);
    }

    public void Reset()
    {
        _mv2.Reset(); _puigChild.Reset(); _scheps73.Reset();
        _api550B.Reset(); _nlsBuss.Reset(); _magicMids.Reset();
        _compBDelay.Reset(); _compCDelay.Reset();
    }

    // ── Latency snapshot ──────────────────────────────────────────────────────

    private IReadOnlyList<NodeLatencyInfo> BuildLatencySnapshot() =>
    [
        new("upper_range", "Upper Range",   0,                            0),
        new("mv2",         "MV2",           Mv2Node.SelfLatency,          _mv2.TotalLatency),
        new("puig670",     "PuigChild 670", PuigChild670Node.SelfLatency, _puigChild.TotalLatency),
        new("scheps73",    "Scheps 73",     Scheps73Node.SelfLatency,     _scheps73.TotalLatency),
        new("api550b",     "API-550B",      Api550BNode.SelfLatency,      _api550B.TotalLatency),
        new("nls",         "NLS Buss",      NlsBussNode.SelfLatency,      _nlsBuss.TotalLatency),
        new("comp_b",      "Comp Delay B",  MaxLatency - ChainBLatency,   MaxLatency),
        new("comp_c",      "Comp Delay C",  MaxLatency - ChainCLatency,   MaxLatency),
        new("magic_mids",  "Magic Mids",    MagicMidsNode.SelfLatency,    MaxLatency),
    ];
}
