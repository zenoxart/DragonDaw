using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DAW.MVVM.Models.PianoRoll;

namespace DAW.MVVM.Models.Sequencer;

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

    // ── Voice cutting ("Group", FL-Studio style) ───────────────────────────────
    // Cut       = the choke-group this channel's own ringing voice belongs to.
    // CutBy     = the choke-group this channel's trigger silences (0 = none).
    //             e.g. closed hi-hat CutBy=1 chokes an open hi-hat whose Cut=1.
    // CutSelf   = also choke this channel's own previous voice on retrigger,
    //             independent of the group — fixes overlapping/phasey re-hits.
    private int  _cutGroup;
    private int  _cutByGroup;
    private bool _cutSelf;

    /// <summary>Choke-group this channel's own voice belongs to (0 = None, 1–8).</summary>
    public int CutGroup
    {
        get => _cutGroup;
        set => SetField(ref _cutGroup, Math.Clamp(value, 0, 8));
    }

    /// <summary>Choke-group this channel's trigger silences (0 = None, 1–8).</summary>
    public int CutByGroup
    {
        get => _cutByGroup;
        set => SetField(ref _cutByGroup, Math.Clamp(value, 0, 8));
    }

    /// <summary>When true, retriggering this channel chokes its own still-ringing voice.</summary>
    public bool CutSelf
    {
        get => _cutSelf;
        set => SetField(ref _cutSelf, value);
    }

    // ── Amplitude envelope ───────────────────────────────────────────────────
    // Disabled by default so existing channels keep their current flat-gain
    // behaviour. When enabled, shapes the voice Delay→Attack→Hold→Decay→Sustain,
    // and its Release/ReleaseTension also become the fade used whenever this
    // voice is choked (by itself or by a Cut group) instead of a hard, clicky cut.
    private bool  _envelopeEnabled;
    private float _envDelay;
    private float _envAttack   = 0.001f;
    private float _envHold;
    private float _envDecay    = 0.30f;
    private float _envSustain  = 1f;
    private float _envRelease  = 0.05f;
    private float _envAttackTension;
    private float _envReleaseTension;

    public bool EnvelopeEnabled
    {
        get => _envelopeEnabled;
        set => SetField(ref _envelopeEnabled, value);
    }

    /// <summary>Seconds of silence before the attack starts (0–2s).</summary>
    public float EnvDelay
    {
        get => _envDelay;
        set => SetField(ref _envDelay, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>Attack time in seconds (0–2s).</summary>
    public float EnvAttack
    {
        get => _envAttack;
        set => SetField(ref _envAttack, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>Seconds held at full level after the attack (0–2s).</summary>
    public float EnvHold
    {
        get => _envHold;
        set => SetField(ref _envHold, Math.Clamp(value, 0f, 2f));
    }

    /// <summary>Decay time from full level down to Sustain, in seconds (0–4s).</summary>
    public float EnvDecay
    {
        get => _envDecay;
        set => SetField(ref _envDecay, Math.Clamp(value, 0f, 4f));
    }

    /// <summary>Sustain level (0–1) held until the voice is choked or the sample ends.</summary>
    public float EnvSustain
    {
        get => _envSustain;
        set => SetField(ref _envSustain, Math.Clamp(value, 0f, 1f));
    }

    /// <summary>Release/fade-out time in seconds (0–4s) — also used as the choke fade.</summary>
    public float EnvRelease
    {
        get => _envRelease;
        set => SetField(ref _envRelease, Math.Clamp(value, 0f, 4f));
    }

    /// <summary>Attack curve bend (-1 = logarithmic, 0 = linear, +1 = exponential).</summary>
    public float EnvAttackTension
    {
        get => _envAttackTension;
        set => SetField(ref _envAttackTension, Math.Clamp(value, -1f, 1f));
    }

    /// <summary>Release curve bend (-1 = logarithmic, 0 = linear, +1 = exponential).</summary>
    public float EnvReleaseTension
    {
        get => _envReleaseTension;
        set => SetField(ref _envReleaseTension, Math.Clamp(value, -1f, 1f));
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
