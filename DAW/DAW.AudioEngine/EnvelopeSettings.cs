namespace DAW.Audio;

/// <summary>
/// Immutable snapshot of a Channel-Rack channel's amplitude envelope
/// (Delay &#8594; Attack &#8594; Hold &#8594; Decay &#8594; Sustain, with Release/ReleaseTension
/// also used as the fade whenever a voice is choked). All times are in seconds;
/// Sustain is a linear level (0&#8211;1); the two Tension values bend the Attack and
/// Release curves (-1 = logarithmic, 0 = linear, +1 = exponential).
/// </summary>
public readonly record struct EnvelopeSettings(
    float Delay,
    float Attack,
    float Hold,
    float Decay,
    float Sustain,
    float Release,
    float AttackTension,
    float ReleaseTension)
{
    /// <summary>Fast, click-free default: no delay/hold, instant-ish attack, short decay to unity, quick release.</summary>
    public static readonly EnvelopeSettings Default =
        new(0f, 0.001f, 0f, 0.30f, 1f, 0.05f, 0f, 0f);
}
