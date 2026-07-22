namespace DAW.Audio.Effects;

/// <summary>
/// Filter mode for a single EQ band (similar to FL Studio Parametric EQ 2).
/// </summary>
public enum EqBandMode
{
    /// <summary>Peaking / Bell filter — boosts or cuts around a center frequency.</summary>
    Peaking,
    /// <summary>Low Shelf — boosts or cuts below the frequency.</summary>
    LowShelf,
    /// <summary>High Shelf — boosts or cuts above the frequency.</summary>
    HighShelf,
    /// <summary>Low Cut (High Pass) — removes frequencies below the cutoff.</summary>
    LowCut,
    /// <summary>High Cut (Low Pass) — removes frequencies above the cutoff.</summary>
    HighCut,
    /// <summary>Notch — removes a narrow band of frequencies.</summary>
    Notch,
    /// <summary>Band Pass — passes only a narrow band of frequencies.</summary>
    BandPass
}
