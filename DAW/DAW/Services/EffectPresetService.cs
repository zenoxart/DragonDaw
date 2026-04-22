using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DAW.Audio.Effects;

namespace DAW.Services;

/// <summary>
/// Preset data model stored as JSON.
/// </summary>
public class EffectPreset
{
    public string Name { get; set; } = string.Empty;
    public string EffectType { get; set; } = string.Empty;
    public Dictionary<string, JsonElement> Parameters { get; set; } = [];
}

/// <summary>
/// Service for saving, loading, and listing effect presets.
/// Presets are stored as JSON in %AppData%/Lapis DAW/Presets/{EffectType}/
/// </summary>
public static class EffectPresetService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Properties to skip when serializing (base class, metering, internal state)
    private static readonly HashSet<string> SkipProperties =
    [
        "Name", "IsEnabled", "IsExpanded", "EffectType", "Icon",
        // Compressor metering (read-only state, not parameters)
        "GainReduction", "InputLevel", "OutputLevel", "MeterMode",
        // EQ internal state
        "SpectrumData", "LastSampleRate", "Bands",
        // Reverb legacy compat (handled via the new params)
        "RoomSize", "Damping", "WetLevel", "DryLevel", "Width"
    ];

    private static string GetPresetsFolder(string effectType)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "Lapis DAW", "Presets", effectType);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Lists all available presets for an effect type.
    /// Returns preset names (without .json extension).
    /// </summary>
    public static List<string> ListPresets(string effectType)
    {
        var folder = GetPresetsFolder(effectType);
        var presets = new List<string>();
        try
        {
            foreach (var file in Directory.GetFiles(folder, "*.json"))
                presets.Add(Path.GetFileNameWithoutExtension(file));
            presets.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch { /* folder may not exist yet */ }
        return presets;
    }

    /// <summary>
    /// Saves the current effect parameters as a named preset.
    /// </summary>
    public static void SavePreset(AudioEffect effect, string presetName)
    {
        var preset = new EffectPreset
        {
            Name = presetName,
            EffectType = effect.EffectType
        };

        foreach (var prop in GetSerializableProperties(effect))
        {
            try
            {
                var value = prop.GetValue(effect);
                if (value != null)
                {
                    var json = JsonSerializer.SerializeToElement(value, JsonOpts);
                    preset.Parameters[prop.Name] = json;
                }
            }
            catch { /* skip unserializable properties */ }
        }

        var folder = GetPresetsFolder(effect.EffectType);
        var safeName = SanitizeFileName(presetName);
        var path = Path.Combine(folder, safeName + ".json");
        var jsonStr = JsonSerializer.Serialize(preset, JsonOpts);
        File.WriteAllText(path, jsonStr);
        effect.CurrentPresetName = presetName;
    }

    /// <summary>
    /// Loads a named preset and applies it to the given effect.
    /// </summary>
    public static bool LoadPreset(AudioEffect effect, string presetName)
    {
        var folder = GetPresetsFolder(effect.EffectType);
        var safeName = SanitizeFileName(presetName);
        var path = Path.Combine(folder, safeName + ".json");

        if (!File.Exists(path)) return false;

        try
        {
            var jsonStr = File.ReadAllText(path);
            var preset = JsonSerializer.Deserialize<EffectPreset>(jsonStr, JsonOpts);
            if (preset == null || preset.EffectType != effect.EffectType) return false;

            ApplyPreset(effect, preset);
            effect.CurrentPresetName = presetName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a named preset.
    /// </summary>
    public static bool DeletePreset(string effectType, string presetName)
    {
        var folder = GetPresetsFolder(effectType);
        var safeName = SanitizeFileName(presetName);
        var path = Path.Combine(folder, safeName + ".json");

        try
        {
            if (File.Exists(path)) { File.Delete(path); return true; }
        }
        catch { /* ignore */ }
        return false;
    }

    /// <summary>
    /// Applies preset parameter values to an effect instance.
    /// </summary>
    private static void ApplyPreset(AudioEffect effect, EffectPreset preset)
    {
        var props = GetSerializableProperties(effect).ToDictionary(p => p.Name);

        foreach (var (name, jsonValue) in preset.Parameters)
        {
            if (!props.TryGetValue(name, out var prop)) continue;

            try
            {
                var value = jsonValue.Deserialize(prop.PropertyType, JsonOpts);
                if (value != null)
                    prop.SetValue(effect, value);
            }
            catch { /* skip incompatible values */ }
        }

        // Force re-initialization for effects with delay buffers
        effect.Reset();
    }

    private static IEnumerable<PropertyInfo> GetSerializableProperties(AudioEffect effect)
    {
        return effect.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite
                        && p.GetIndexParameters().Length == 0
                        && !SkipProperties.Contains(p.Name)
                        && IsSimpleType(p.PropertyType));
    }

    private static bool IsSimpleType(Type t) =>
        t == typeof(double) || t == typeof(float) || t == typeof(int) ||
        t == typeof(bool) || t == typeof(string) || t.IsEnum;

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized.Trim();
    }
}
