using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DAW.Models.PianoRoll;

namespace DAW.Models.Sequencer;

/// <summary>
/// Represents one instrument channel inside a pattern.
/// Maps 1-to-1 with a row in the Channel Rack.
/// </summary>
public class ChannelModel : INotifyPropertyChanged
{
    private string  _name         = "Channel";
    private int     _mixerTrack   = 0;       // 0 = master
    private bool    _isMuted      = false;
    private bool    _isSolo       = false;
    private Color   _channelColor = Colors.DodgerBlue;
    private string  _pluginIcon   = "🎹";
    private float   _defaultVelocity = 1.0f;
    private float   _volume       = 1.0f;
    private int     _stepCount    = 16;

    // ── Identity ─────────────────────────────────────────────────────────────

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public int MixerTrack
    {
        get => _mixerTrack;
        set => SetField(ref _mixerTrack, value);
    }

    /// <summary>Emoji or path for the instrument icon displayed in the channel strip.</summary>
    public string PluginIcon
    {
        get => _pluginIcon;
        set => SetField(ref _pluginIcon, value);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    public bool IsSolo
    {
        get => _isSolo;
        set => SetField(ref _isSolo, value);
    }

    public Color ChannelColor
    {
        get => _channelColor;
        set => SetField(ref _channelColor, value);
    }

    private string _samplePath = string.Empty;

    /// <summary>Path to the audio file loaded into this channel.</summary>
    public string SamplePath
    {
        get => _samplePath;
        set => SetField(ref _samplePath, value);
    }

    /// <summary>Piano Roll notes for this channel (melody / custom notes).</summary>
    public ObservableCollection<PianoRollNote> PianoRollNotes { get; } = [];

    public float DefaultVelocity
    {
        get => _defaultVelocity;
        set => SetField(ref _defaultVelocity, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Per-channel volume (0–1) applied before the sample reaches the mixer.</summary>
    public float Volume
    {
        get => _volume;
        set => SetField(ref _volume, Math.Clamp(value, 0f, 1f));
    }

    // ── Steps ─────────────────────────────────────────────────────────────────

    /// <summary>The steps collection. Normally 16, 32, or 64 steps.</summary>
    public ObservableCollection<StepModel> Steps { get; } = [];

    public int StepCount
    {
        get => _stepCount;
        set
        {
            int old = _stepCount;
            if (!SetField(ref _stepCount, Math.Clamp(value, 1, 256))) return;
            ResizeSteps(old, _stepCount);
        }
    }

    private void ResizeSteps(int oldCount, int newCount)
    {
        while (Steps.Count < newCount)
            Steps.Add(new StepModel());
        while (Steps.Count > newCount)
            Steps.RemoveAt(Steps.Count - 1);
    }

    // ── Constructor ───────────────────────────────────────────────────────────

    public ChannelModel(string name = "Channel", int stepCount = 16)
    {
        _name      = name;
        _stepCount = stepCount;
        for (int i = 0; i < stepCount; i++)
            Steps.Add(new StepModel());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
