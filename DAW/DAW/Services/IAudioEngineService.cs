namespace DAW.Services;

/// <summary>
/// Interface for the audio engine service.
/// Handles real-time audio processing commands.
/// </summary>
public interface IAudioEngineService
{
    /// <summary>
    /// Updates the mix (dry/wet) value for an effect slot
    /// </summary>
    void UpdateSlotMix(string channelId, int slotIndex, float mixValue);
    
    /// <summary>
    /// Updates the bypass state for an effect slot
    /// </summary>
    void UpdateSlotBypass(string channelId, int slotIndex, bool isActive);
    
    /// <summary>
    /// Loads a plugin into an effect slot
    /// </summary>
    void LoadPlugin(string channelId, int slotIndex, string pluginId);
    
    /// <summary>
    /// Removes a plugin from an effect slot
    /// </summary>
    void UnloadPlugin(string channelId, int slotIndex);
    
    /// <summary>
    /// Updates a plugin parameter
    /// </summary>
    void UpdatePluginParameter(string channelId, int slotIndex, string parameterName, float value);
}
