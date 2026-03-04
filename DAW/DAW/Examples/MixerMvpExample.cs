using DAW.Models.Mixer;
using DAW.Presenters;
using DAW.Services;
using DAW.Views.Mixer;

namespace DAW.Examples;

/// <summary>
/// Example demonstrating the complete MVP interaction chain for the Mixer Effect Rack.
/// 
/// Architecture Overview:
/// ┌─────────────────────────────────────────────────────────────────┐
/// │                         PROJECT MODEL                          │
/// │  ┌─────────────────────────────────────────────────────────┐   │
/// │  │                     MixerModel                          │   │
/// │  │  SelectedChannelIndex: int                              │   │
/// │  │  ┌─────────────────────────────────────────────────┐    │   │
/// │  │  │        MixerChannelModel (Master/Track)         │    │   │
/// │  │  │  Name: string                                   │    │   │
/// │  │  │  ┌─────────────────────────────────────────┐    │    │   │
/// │  │  │  │   EffectSlotModel[10]                   │    │    │   │
/// │  │  │  │   - SlotIndex: int                      │    │    │   │
/// │  │  │  │   - PluginId: string?                   │    │    │   │
/// │  │  │  │   - IsActive: bool                      │    │    │   │
/// │  │  │  │   - SlotMix: float                      │    │    │   │
/// │  │  │  │   - Parameters: Dictionary              │    │    │   │
/// │  │  │  └─────────────────────────────────────────┘    │    │   │
/// │  │  └─────────────────────────────────────────────────┘    │   │
/// │  └─────────────────────────────────────────────────────────┘   │
/// └─────────────────────────────────────────────────────────────────┘
///                              │
///                              │ PropertyChanged events
///                              ▼
/// ┌─────────────────────────────────────────────────────────────────┐
/// │                         PRESENTER                               │
/// │  MixerEffectRackPresenter                                      │
/// │  - Subscribes to Model changes                                 │
/// │  - Transforms Model → ViewData                                 │
/// │  - Handles View events                                         │
/// │  - Updates Model                                               │
/// │  - Notifies AudioEngine                                        │
/// └─────────────────────────────────────────────────────────────────┘
///                    │                      │
///        View Events │                      │ AudioEngine Commands
///                    ▼                      ▼
/// ┌────────────────────────┐    ┌─────────────────────────────────┐
/// │  IMixerEffectRackView  │    │      IAudioEngineService        │
/// │  (MixerEffectRackView) │    │    (MockAudioEngineService)     │
/// │                        │    │                                 │
/// │  Events:               │    │  - UpdateSlotMix()              │
/// │  - SlotExpandToggled   │    │  - UpdateSlotBypass()           │
/// │  - SlotBypassChanged   │    │  - LoadPlugin()                 │
/// │  - SlotMixChanged      │    │  - UnloadPlugin()               │
/// │                        │    │                                 │
/// │  Methods:              │    └─────────────────────────────────┘
/// │  - SetTitle()          │
/// │  - SetSlots()          │
/// │  - UpdateSlot()        │
/// └────────────────────────┘
/// </summary>
public static class MixerMvpExample
{
    /// <summary>
    /// Demonstrates the complete interaction chain.
    /// </summary>
    public static void RunExample()
    {
        Console.WriteLine("=== Mixer MVP Architecture Example ===\n");
        
        // 1. Create the Model
        var mixerModel = CreateSampleModel();
        
        // 2. Create the AudioEngine service
        var audioEngine = new MockAudioEngineService();
        audioEngine.CommandExecuted += (_, msg) => Console.WriteLine($"  {msg}");
        
        // 3. Create the View (mock implementation for console demo)
        var view = new ConsoleMixerView();
        
        // 4. Create the Presenter (connects View ↔ Model ↔ AudioEngine)
        using var presenter = new MixerEffectRackPresenter(view, mixerModel, audioEngine);
        
        Console.WriteLine("\n--- Initial State ---");
        Console.WriteLine($"Title: {view.CurrentTitle}");
        Console.WriteLine($"Slots: {view.CurrentSlots?.Count ?? 0}");
        
        // === Interaction Chain 1: Channel Switch ===
        Console.WriteLine("\n\n=== INTERACTION 1: Channel Switch ===");
        Console.WriteLine("User selects Track 1...\n");
        
        // This triggers: Model change → Presenter → View update
        mixerModel.SelectChannel(0); // Select first track
        
        Console.WriteLine($"\nResult: Title = '{view.CurrentTitle}'");
        
        // === Interaction Chain 2: Mix Knob Change ===
        Console.WriteLine("\n\n=== INTERACTION 2: Mix Knob Change ===");
        Console.WriteLine("User changes Slot 1 mix to 75%...\n");
        
        // This triggers: View event → Presenter → Model update + AudioEngine
        view.SimulateMixChange(1, 0.75f);
        
        var slot1 = mixerModel.SelectedChannel?.GetSlot(1);
        Console.WriteLine($"\nResult: Model SlotMix = {slot1?.SlotMix:P0}");
        
        // === Interaction Chain 3: Bypass Change ===
        Console.WriteLine("\n\n=== INTERACTION 3: Bypass Toggle ===");
        Console.WriteLine("User bypasses Slot 1...\n");
        
        // This triggers: View event → Presenter → Model update + AudioEngine
        view.SimulateBypassChange(1, false);
        
        Console.WriteLine($"\nResult: Model IsActive = {slot1?.IsActive}");
        
        // === Loading from JSON ===
        Console.WriteLine("\n\n=== LOADING FROM JSON ===");
        Console.WriteLine("Simulating project load from JSON...\n");
        
        // When loading a project, the Model is populated from JSON
        // Then ForceRefresh() is called to update the View
        slot1!.PluginName = "Loaded EQ";
        slot1.SlotMix = 0.5f;
        slot1.IsActive = true;
        
        presenter.ForceRefresh();
        
        Console.WriteLine($"View updated with loaded data");
        Console.WriteLine($"Slot 1: {view.CurrentSlots?[0].PluginName}, Mix={view.CurrentSlots?[0].SlotMix:P0}");
        
        Console.WriteLine("\n=== Example Complete ===");
    }
    
    private static MixerModel CreateSampleModel()
    {
        var model = new MixerModel();
        
        // Add some effects to Master
        var masterSlot1 = model.MasterChannel.GetSlot(1)!;
        masterSlot1.PluginId = "com.example.eq";
        masterSlot1.PluginName = "Master EQ";
        masterSlot1.PluginIcon = "📊";
        masterSlot1.SlotMix = 1.0f;
        
        var masterSlot2 = model.MasterChannel.GetSlot(2)!;
        masterSlot2.PluginId = "com.example.limiter";
        masterSlot2.PluginName = "Master Limiter";
        masterSlot2.PluginIcon = "📉";
        masterSlot2.SlotMix = 1.0f;
        
        // Add Track 1
        var track1 = model.AddChannel("Track 1");
        var trackSlot1 = track1.GetSlot(1)!;
        trackSlot1.PluginId = "com.example.comp";
        trackSlot1.PluginName = "Track Compressor";
        trackSlot1.PluginIcon = "📉";
        trackSlot1.SlotMix = 0.8f;
        
        return model;
    }
}

/// <summary>
/// Console-based mock implementation of IMixerEffectRackView for demonstration.
/// In the real application, MixerEffectRackView (WPF UserControl) is used.
/// </summary>
internal class ConsoleMixerView : IMixerEffectRackView
{
    public string CurrentTitle { get; private set; } = string.Empty;
    public IReadOnlyList<EffectSlotViewData>? CurrentSlots { get; private set; }
    
    // Events
    public event EventHandler<int>? SlotExpandToggled;
    public event EventHandler<SlotBypassChangedEventArgs>? SlotBypassChanged;
    public event EventHandler<SlotMixChangedEventArgs>? SlotMixChanged;
    public event EventHandler<int>? SlotAddEffectRequested;
    public event EventHandler<int>? SlotRemoveEffectRequested;
    public event EventHandler<int>? SlotOpenEditorRequested;
    
    // Interface methods
    public void SetTitle(string title)
    {
        CurrentTitle = title;
        Console.WriteLine($"[View] SetTitle: '{title}'");
    }
    
    public void SetSlots(IReadOnlyList<EffectSlotViewData> slots)
    {
        CurrentSlots = slots;
        Console.WriteLine($"[View] SetSlots: {slots.Count} slots loaded");
        foreach (var slot in slots.Where(s => s.HasPlugin))
        {
            Console.WriteLine($"       Slot {slot.SlotIndex}: {slot.PluginName} (Mix={slot.SlotMix:P0}, Active={slot.IsActive})");
        }
    }
    
    public void UpdateSlot(int slotIndex, EffectSlotViewData slotData)
    {
        Console.WriteLine($"[View] UpdateSlot {slotIndex}: {slotData.DisplayText}");
    }
    
    public void ShowLoading(bool isLoading)
    {
        Console.WriteLine($"[View] ShowLoading: {isLoading}");
    }
    
    public void ShowStatus(string message)
    {
        Console.WriteLine($"[View] Status: {message}");
    }
    
    // Simulation methods for testing
    public void SimulateMixChange(int slotIndex, float newMix)
    {
        Console.WriteLine($"[View→Presenter] SlotMixChanged event: Slot={slotIndex}, Mix={newMix:P0}");
        SlotMixChanged?.Invoke(this, new SlotMixChangedEventArgs(slotIndex, newMix));
    }
    
    public void SimulateBypassChange(int slotIndex, bool isActive)
    {
        Console.WriteLine($"[View→Presenter] SlotBypassChanged event: Slot={slotIndex}, Active={isActive}");
        SlotBypassChanged?.Invoke(this, new SlotBypassChangedEventArgs(slotIndex, isActive));
    }
}
