using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using DAW.Audio;
using DAW.Commands;
using DAW.Models.Sequencer;

namespace DAW.ViewModels.Sequencer;

/// <summary>
/// ViewModel for a single channel row in the Channel Rack.
/// Exposes all per-channel properties and wraps the step collection.
/// Owns a <see cref="PreloadedSample"/> that is rebuilt whenever the sample path changes,
/// so the audio callback never needs to touch the disk.
/// </summary>
public class ChannelViewModel : INotifyPropertyChanged
{
    public ChannelModel Model { get; }

    // ── Pre-loaded audio ──────────────────────────────────────────────────────

    /// <summary>
    /// In-memory decoded audio ready for zero-latency playback.
    /// Null when no sample is loaded or the file could not be read.
    /// Assigned from the audio engine via <see cref="PreloadSampleAsync"/>.
    /// </summary>
    public PreloadedSample? PreloadedSample { get; private set; }

    /// <summary>
    /// The engine reference used for (re-)preloading; injected by MainViewModel.
    /// </summary>
    public AudioMixEngine? AudioEngine { get; set; }

    /// <summary>
    /// Decodes the current <see cref="SamplePath"/> into memory on a background thread.
    /// Call this after setting <see cref="AudioEngine"/> and whenever the path changes.
    /// </summary>
    public async Task PreloadSampleAsync()
    {
        var path   = Model.SamplePath;
        var engine = AudioEngine;
        if (engine == null || string.IsNullOrEmpty(path)) { PreloadedSample = null; return; }

        PreloadedSample = await Task.Run(() => engine.Preload(path));
    }

    // ── Step wrappers ─────────────────────────────────────────────────────────

    public ObservableCollection<StepViewModel> Steps { get; } = [];

    // ── Forwarded properties ──────────────────────────────────────────────────

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public bool IsMuted
    {
        get => Model.IsMuted;
        set { Model.IsMuted = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAudible)); }
    }

    public bool IsSolo
    {
        get => Model.IsSolo;
        set { Model.IsSolo = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsAudible)); }
    }

    public Color ChannelColor => Model.ChannelColor;

    public string PluginIcon => Model.PluginIcon;

    public int MixerTrack
    {
        get => Model.MixerTrack;
        set { Model.MixerTrack = value; OnPropertyChanged(); }
    }

    public float Volume
    {
        get => Model.Volume;
        set { Model.Volume = value; OnPropertyChanged(); }
    }

    /// <summary>True when this channel produces sound (not muted, honoring solo).</summary>
    public bool IsAudible => !IsMuted;

    // ── Commands ──────────────────────────────────────────────────────────────

    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleSoloCommand { get; }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ChannelViewModel(ChannelModel model)
    {
        Model = model;

        // Mirror existing steps
        foreach (var s in model.Steps)
            Steps.Add(new StepViewModel(s));

        // Sync when model adds/removes steps (resize)
        model.Steps.CollectionChanged += (_, e) =>
        {
            if (e.NewItems != null)
                foreach (StepModel s in e.NewItems)
                    Steps.Add(new StepViewModel(s));
            if (e.OldItems != null)
                foreach (StepModel s in e.OldItems)
                {
                    var vm = Steps.FirstOrDefault(sv => sv.Model == s);
                    if (vm != null) Steps.Remove(vm);
                }
        };

        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        ToggleSoloCommand = new RelayCommand(() => IsSolo  = !IsSolo);

        model.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
