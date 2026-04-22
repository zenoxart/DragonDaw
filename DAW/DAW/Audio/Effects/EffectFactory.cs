namespace DAW.Audio.Effects;

/// <summary>
/// Factory for creating audio effect instances.
/// </summary>
public static class EffectFactory
{
    /// <summary>
    /// Available effect types.
    /// </summary>
    public static readonly (string Type, string Name, string Icon)[] AvailableEffects =
    [
        ("Equalizer",   "3-Band EQ",   "📊"),
        ("Compressor",  "Compressor",  "📉"),
        ("Reverb",      "Reverb",      "🏛️"),
        ("Delay",       "Delay",       "🔁"),
        ("Gain",        "Gain",        "🔊"),
        ("Saturation",  "Saturation",  "🔥"),
    ];

    /// <summary>
    /// Creates an effect instance by type name.
    /// </summary>
    public static AudioEffect? Create(string effectType)
    {
        return effectType switch
        {
            "Equalizer"  => new EqualizerEffect(),
            "Compressor" => new CompressorEffect(),
            "Reverb"     => new ReverbEffect(),
            "Delay"      => new DelayEffect(),
            "Gain"       => new GainEffect(),
            "Saturation" => new SaturationEffect(),
            _ => null
        };
    }
}
