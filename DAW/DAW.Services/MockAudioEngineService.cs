using DAW.Services;

namespace DAW.Services;

/// <summary>
/// Mock implementation of the AudioEngine service for testing.
/// In a real application, this would communicate with the actual audio processing engine.
/// </summary>
public sealed class MockAudioEngineService : IAudioEngineService
{
    public event EventHandler<string>? CommandExecuted;
    
    public void UpdateSlotMix(string channelId, int slotIndex, float mixValue)
    {
        var msg = $"[AudioEngine] UpdateSlotMix: Channel={channelId}, Slot={slotIndex}, Mix={mixValue:P0}";
        System.Diagnostics.Debug.WriteLine(msg);
        CommandExecuted?.Invoke(this, msg);
    }

    public void UpdateSlotBypass(string channelId, int slotIndex, bool isActive)
    {
        var state = isActive ? "Active" : "Bypassed";
        var msg = $"[AudioEngine] UpdateSlotBypass: Channel={channelId}, Slot={slotIndex}, State={state}";
        System.Diagnostics.Debug.WriteLine(msg);
        CommandExecuted?.Invoke(this, msg);
    }

    public void LoadPlugin(string channelId, int slotIndex, string pluginId)
    {
        var msg = $"[AudioEngine] LoadPlugin: Channel={channelId}, Slot={slotIndex}, Plugin={pluginId}";
        System.Diagnostics.Debug.WriteLine(msg);
        CommandExecuted?.Invoke(this, msg);
    }

    public void UnloadPlugin(string channelId, int slotIndex)
    {
        var msg = $"[AudioEngine] UnloadPlugin: Channel={channelId}, Slot={slotIndex}";
        System.Diagnostics.Debug.WriteLine(msg);
        CommandExecuted?.Invoke(this, msg);
    }

    public void UpdatePluginParameter(string channelId, int slotIndex, string parameterName, float value)
    {
        var msg = $"[AudioEngine] UpdateParameter: Channel={channelId}, Slot={slotIndex}, Param={parameterName}, Value={value}";
        System.Diagnostics.Debug.WriteLine(msg);
        CommandExecuted?.Invoke(this, msg);
    }
}
