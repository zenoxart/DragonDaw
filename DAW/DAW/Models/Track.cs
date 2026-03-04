using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DAW.Audio;
using NAudio.Wave;

namespace DAW.Models;

/// <summary>
/// Represents an audio track in the playlist with NAudio-based playback for effect processing.
/// </summary>
public class Track : INotifyPropertyChanged, IDisposable
{
    private const int MaxEffectSlots = 10;
    
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _filePath = string.Empty;
    private TimeSpan _duration;
    private bool _isPlaying;
    private bool _isAnalyzed;
    private string _analysisResult = string.Empty;
    
    // Mixer properties
    private bool _isEnabled = true;
    private double _volume = 0.8;
    private double _pan = 0.0;
    private bool _isMuted;
    private bool _isSolo;
    private bool _isArmed;
    private System.Windows.Media.Color _channelColor = System.Windows.Media.Colors.DodgerBlue;

    // Playback transform properties
    private float _pitchSemitones = 0.0f;
    private double _playbackSpeed = 1.0;
    private TimeStretchMode _playbackMode = TimeStretchMode.Resample;

    // NAudio playback with effect support
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioFile;
    private RateChangeSampleProvider? _rateChanger;
    private VolumePanSampleProvider? _volumePan;
    private EffectSampleProvider? _effectProvider;
    private bool _isLoaded;
    private int _trackNumber;
    
    // Effect chain for this track
    private bool _showEffects;

    public Track()
    {
        // Initialize effect chain
        EffectChain = new EffectChain();
        
        // Initialize 10 effect slots
        EffectSlots = new ObservableCollection<EffectSlot>();
        for (int i = 1; i <= MaxEffectSlots; i++)
        {
            EffectSlots.Add(new EffectSlot(i));
        }
    }

    /// <summary>
    /// The effect chain for this track.
    /// </summary>
    public EffectChain EffectChain { get; }
    
    /// <summary>
    /// The 10 effect slots for this track's insert chain.
    /// </summary>
    public ObservableCollection<EffectSlot> EffectSlots { get; }
    
    /// <summary>
    /// Whether the effects panel is visible for this track.
    /// </summary>
    public bool ShowEffects
    {
        get => _showEffects;
        set => SetField(ref _showEffects, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        set => SetField(ref _artist, value);
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetField(ref _filePath, value))
            {
                LoadAudio();
            }
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set => SetField(ref _duration, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    public bool IsAnalyzed
    {
        get => _isAnalyzed;
        set => SetField(ref _isAnalyzed, value);
    }

    public string AnalysisResult
    {
        get => _analysisResult;
        set => SetField(ref _analysisResult, value);
    }
    
    public int TrackNumber
    {
        get => _trackNumber;
        set => SetField(ref _trackNumber, value);
    }
    
    /// <summary>
    /// Whether this track is active. When false the audio output is silenced (bypassed).
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetField(ref _isEnabled, value))
                UpdatePlayerVolume();
        }
    }

    /// <summary>
    /// Pitch shift in semitones (-24 to +24). 0 = no shift.
    /// Updating this value is thread-safe: the change propagates to the audio thread via a volatile write.
    /// </summary>
    public float PitchSemitones
    {
        get => _pitchSemitones;
        set
        {
            if (SetField(ref _pitchSemitones, Math.Clamp(value, -24f, 24f)))
            {
                UpdateRateChange();
                OnPropertyChanged(nameof(PitchDisplay));
            }
        }
    }

    /// <summary>Pitch display string, e.g. "+3", "-12", "0".</summary>
    public string PitchDisplay => _pitchSemitones switch
    {
        > 0.05f => $"+{_pitchSemitones:F0}",
        < -0.05f => $"{_pitchSemitones:F0}",
        _ => "0"
    };

    /// <summary>
    /// Playback speed multiplier (0.25x – 4.0x). 1.0 = normal speed.
    /// In Resample mode this also affects pitch; in TimeStretch mode only the tempo changes.
    /// </summary>
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetField(ref _playbackSpeed, Math.Clamp(value, 0.25, 4.0)))
            {
                UpdateRateChange();
                OnPropertyChanged(nameof(PlaybackSpeedDisplay));
            }
        }
    }

    /// <summary>Speed display string, e.g. "1.00x".</summary>
    public string PlaybackSpeedDisplay => $"{_playbackSpeed:F2}x";

    /// <summary>
    /// Algorithm used when Pitch or Speed are not at their default values.
    /// <see cref="TimeStretchMode.Resample"/>: a single resampling pass – pitch and time change together.
    /// <see cref="TimeStretchMode.TimeStretch"/>: only the speed multiplier is applied via resampling;
    /// independent pitch-only shifting requires a phase-vocoder (future extension).
    /// </summary>
    public TimeStretchMode PlaybackMode
    {
        get => _playbackMode;
        set
        {
            if (SetField(ref _playbackMode, value))
                UpdateRateChange();
        }
    }

    public bool IsArmed
    {
        get => _isArmed;
        set => SetField(ref _isArmed, value);
    }
    
    public bool IsLoaded
    {
        get => _isLoaded;
        set => SetField(ref _isLoaded, value);
    }

    public string DisplayDuration => Duration.ToString(@"mm\:ss");

    /// <summary>
    /// Volume level from 0.0 (silent) to 2.0 (≈ +6 dB gain).
    /// 1.0 = unity gain (0 dB).  Stored as linear amplitude; use <see cref="VolumeDisplay"/> for dB.
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetField(ref _volume, Math.Clamp(value, 0.0, 2.0)))
            {
                UpdatePlayerVolume();
                OnPropertyChanged(nameof(VolumeDisplay));
            }
        }
    }

    /// <summary>
    /// Pan position from -1.0 (full left) to 1.0 (full right). 0.0 is center.
    /// </summary>
    public double Pan
    {
        get => _pan;
        set
        {
            if (SetField(ref _pan, Math.Clamp(value, -1.0, 1.0)))
            {
                if (_volumePan != null)
                {
                    _volumePan.Pan = (float)_pan;
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetField(ref _isMuted, value))
            {
                UpdatePlayerVolume();
            }
        }
    }

    public bool IsSolo
    {
        get => _isSolo;
        set => SetField(ref _isSolo, value);
    }

    public System.Windows.Media.Color ChannelColor
    {
        get => _channelColor;
        set => SetField(ref _channelColor, value);
    }

    /// <summary>
    /// Volume displayed in dB (decibels). -60dB = silence, 0dB = full.
    /// </summary>
    public string VolumeDisplay => Volume > 0 
        ? $"{20 * Math.Log10(Volume):F1} dB" 
        : "-∞ dB";

    /// <summary>
    /// Pan displayed as L/C/R indicator.
    /// </summary>
    public string PanDisplay => Pan switch
    {
        < -0.01 => $"L {Math.Abs(Pan) * 100:F0}",
        > 0.01 => $"R {Pan * 100:F0}",
        _ => "C"
    };
    
    /// <summary>
    /// Recalculates and applies the effective playback rate to <see cref="_rateChanger"/>.
    /// Called whenever PitchSemitones, PlaybackSpeed, or PlaybackMode changes.
    /// Lock-free: propagates to the audio thread via a volatile write inside RateChangeSampleProvider.
    /// </summary>
    private void UpdateRateChange()
    {
        if (_rateChanger is null) return;

        float pitchRatio = MathF.Pow(2f, _pitchSemitones / 12f);
        float timeRatio  = (float)_playbackSpeed;

        _rateChanger.Rate = _playbackMode switch
        {
            TimeStretchMode.Resample => pitchRatio * timeRatio,
            _ => timeRatio   // TimeStretch: time-only resampling; independent pitch shift requires phase-vocoder
        };
    }

    /// <summary>
    /// Loads audio file into this track's player with effect chain.
    /// </summary>
    private void LoadAudio()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        
        try
        {
            // Cleanup previous playback
            DisposeAudio();
            
            // Create audio file reader
            _audioFile = new AudioFileReader(FilePath);
            Duration = _audioFile.TotalTime;

            // Insert rate-change provider directly after the file reader.
            // Rate is a volatile float — safe to update from the UI thread while the audio thread reads it.
            _rateChanger = new RateChangeSampleProvider(_audioFile);
            UpdateRateChange();

            // Create effect chain provider
            _effectProvider = new EffectSampleProvider(_rateChanger, EffectChain);
            
            // Create volume/pan provider
            _volumePan = new VolumePanSampleProvider(_effectProvider);
            _volumePan.Volume = (float)Volume;
            _volumePan.Pan = (float)Pan;
            
            // Create output device
            _waveOut = new WaveOutEvent();
            _waveOut.Init(_volumePan);
            _waveOut.PlaybackStopped += (_, _) =>
            {
                if (_audioFile != null && _audioFile.Position >= _audioFile.Length)
                {
                    // Track ended naturally
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsPlaying = false;
                        _audioFile.Position = 0;
                    });
                }
            };
            
            IsLoaded = true;
        }
        catch
        {
            IsLoaded = false;
        }
    }
    
    private void DisposeAudio()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        
        _audioFile?.Dispose();
        _audioFile = null;

        _rateChanger = null;    // RateChangeSampleProvider is not IDisposable
        _volumePan = null;
        _effectProvider = null;
    }
    
    /// <summary>
    /// Updates the volume and pan based on track settings.
    /// </summary>
    public void UpdatePlayerVolume(double masterVolume = 1.0, bool hasSoloTracks = false)
    {
        if (_volumePan is null) return;
        
        float effectiveVolume = 0;

        if (!IsEnabled || IsMuted || (hasSoloTracks && !IsSolo))
        {
            effectiveVolume = 0;
        }
        else
        {
            effectiveVolume = (float)(Volume * masterVolume);
        }
        
        _volumePan.Volume = effectiveVolume;
        _volumePan.Pan = (float)Pan;
    }
    
    /// <summary>
    /// Plays this track from current position.
    /// </summary>
    public void Play()
    {
        if (_waveOut is null && !string.IsNullOrEmpty(FilePath))
        {
            LoadAudio();
        }
        
        _waveOut?.Play();
        IsPlaying = true;
    }
    
    /// <summary>
    /// Pauses this track.
    /// </summary>
    public void Pause()
    {
        _waveOut?.Pause();
        IsPlaying = false;
    }
    
    /// <summary>
    /// Stops this track and resets position.
    /// </summary>
    public void Stop()
    {
        _waveOut?.Stop();
        if (_audioFile is not null)
        {
            _audioFile.Position = 0;
        }
        IsPlaying = false;
    }
    
    /// <summary>
    /// Sets playback position.
    /// </summary>
    public void SetPosition(TimeSpan position)
    {
        if (_audioFile is not null)
        {
            _audioFile.CurrentTime = position;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    
    public void Dispose()
    {
        DisposeAudio();
    }
}
