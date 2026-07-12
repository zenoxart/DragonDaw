using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.Models;

/// <summary>
/// Represents the audio clip data with all sampler/clip settings.
/// This is the central data model for the Sampler/Clip Editor.
/// Separates real-time parameters from offline-processed data.
/// </summary>
public class ClipData : INotifyPropertyChanged
{
    #region Source File Information
    
    private string _sourceFilePath = string.Empty;
    private string _displayName = string.Empty;
    private TimeSpan _originalDuration;
    private int _sampleRate;
    private int _channels;
    private int _bitDepth;
    private long _totalSamples;
    
    /// <summary>Path to the original audio file.</summary>
    public string SourceFilePath
    {
        get => _sourceFilePath;
        set => SetField(ref _sourceFilePath, value);
    }
    
    /// <summary>Display name for the clip.</summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }
    
    /// <summary>Original duration before any processing.</summary>
    public TimeSpan OriginalDuration
    {
        get => _originalDuration;
        set => SetField(ref _originalDuration, value);
    }
    
    /// <summary>Sample rate of the audio file.</summary>
    public int SampleRate
    {
        get => _sampleRate;
        set => SetField(ref _sampleRate, value);
    }
    
    /// <summary>Number of audio channels.</summary>
    public int Channels
    {
        get => _channels;
        set => SetField(ref _channels, value);
    }
    
    /// <summary>Bit depth of the audio.</summary>
    public int BitDepth
    {
        get => _bitDepth;
        set => SetField(ref _bitDepth, value);
    }
    
    /// <summary>Total number of samples in the file.</summary>
    public long TotalSamples
    {
        get => _totalSamples;
        set => SetField(ref _totalSamples, value);
    }
    
    #endregion
    
    #region Real-Time Parameters (Audio Thread Safe)
    
    private volatile bool _isEnabled = true;
    private volatile float _volume = 1.0f;
    private volatile float _pan = 0.0f;
    private volatile float _pitchSemitones = 0.0f;
    private volatile float _pitchRangeMin = -12.0f;
    private volatile float _pitchRangeMax = 12.0f;
    
    /// <summary>Whether the clip is active (bypassed if false). Thread-safe.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }
    
    /// <summary>Volume level (0.0 - 2.0). Thread-safe for real-time access.</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 2f);
            if (Math.Abs(_volume - clamped) > 0.0001f)
            {
                _volume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeDb));
            }
        }
    }
    
    /// <summary>Volume in decibels.</summary>
    public string VolumeDb => _volume > 0 
        ? $"{20 * Math.Log10(_volume):F1} dB" 
        : "-∞ dB";
    
    /// <summary>Pan position (-1.0 left to 1.0 right). Thread-safe.</summary>
    public float Pan
    {
        get => _pan;
        set
        {
            float clamped = Math.Clamp(value, -1f, 1f);
            if (Math.Abs(_pan - clamped) > 0.0001f)
            {
                _pan = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PanDisplay));
            }
        }
    }
    
    /// <summary>Pan display string.</summary>
    public string PanDisplay => _pan switch
    {
        < -0.01f => $"L {Math.Abs(_pan) * 100:F0}",
        > 0.01f => $"R {_pan * 100:F0}",
        _ => "C"
    };
    
    /// <summary>Pitch shift in semitones (-24 to +24). Thread-safe.</summary>
    public float PitchSemitones
    {
        get => _pitchSemitones;
        set
        {
            float clamped = Math.Clamp(value, -24f, 24f);
            if (Math.Abs(_pitchSemitones - clamped) > 0.0001f)
            {
                _pitchSemitones = clamped;
                OnPropertyChanged();
            }
        }
    }
    
    /// <summary>Minimum pitch range for modulation.</summary>
    public float PitchRangeMin
    {
        get { var temp = _pitchRangeMin; return temp; }
        set => SetField(ref _pitchRangeMin, Math.Clamp(value, -48f, 0f));
    }
    
    /// <summary>Maximum pitch range for modulation.</summary>
    public float PitchRangeMax
    {
        get { var temp = _pitchRangeMax; return temp; }
        set => SetField(ref _pitchRangeMax, Math.Clamp(value, 0f, 48f));
    }
    
    #endregion
    
    #region Playback Settings
    
    private long _startOffsetSamples;
    private long _loopStartSamples;
    private long _loopEndSamples;
    private bool _loopEnabled;
    private bool _pingPongLoop;
    private bool _reversePlayback;
    
    /// <summary>Start offset in samples.</summary>
    public long StartOffsetSamples
    {
        get => _startOffsetSamples;
        set => SetField(ref _startOffsetSamples, Math.Max(0, value));
    }
    
    /// <summary>Start offset as TimeSpan.</summary>
    public TimeSpan StartOffset => SampleRate > 0 
        ? TimeSpan.FromSeconds((double)_startOffsetSamples / SampleRate) 
        : TimeSpan.Zero;
    
    /// <summary>Loop start point in samples.</summary>
    public long LoopStartSamples
    {
        get => _loopStartSamples;
        set => SetField(ref _loopStartSamples, Math.Max(0, value));
    }
    
    /// <summary>Loop end point in samples.</summary>
    public long LoopEndSamples
    {
        get => _loopEndSamples;
        set => SetField(ref _loopEndSamples, Math.Max(0, value));
    }
    
    /// <summary>Whether looping is enabled.</summary>
    public bool LoopEnabled
    {
        get => _loopEnabled;
        set => SetField(ref _loopEnabled, value);
    }
    
    /// <summary>Whether to use ping-pong (bidirectional) looping.</summary>
    public bool PingPongLoop
    {
        get => _pingPongLoop;
        set => SetField(ref _pingPongLoop, value);
    }
    
    /// <summary>Whether to play in reverse.</summary>
    public bool ReversePlayback
    {
        get => _reversePlayback;
        set => SetField(ref _reversePlayback, value);
    }
    
    #endregion
    
    #region Time Stretching Settings
    
    private TimeStretchMode _timeStretchMode = TimeStretchMode.Resample;
    private double _timeStretchRatio = 1.0;
    private double _timeStretchMultiplier = 1.0;
    
    /// <summary>Time stretching algorithm mode.</summary>
    public TimeStretchMode TimeStretchMode
    {
        get => _timeStretchMode;
        set
        {
            if (SetField(ref _timeStretchMode, value))
            {
                OnPropertyChanged(nameof(IsResampleMode));
                OnPropertyChanged(nameof(IsTimeStretchMode));
            }
        }
    }

    /// <summary>
    /// Helper bool for TwoWay ToggleButton binding: true when mode is <see cref="TimeStretchMode.Resample"/>.
    /// Setting to false is silently ignored — use <see cref="IsTimeStretchMode"/> to switch.
    /// </summary>
    public bool IsResampleMode
    {
        get => _timeStretchMode == TimeStretchMode.Resample;
        set
        {
            if (value)
                TimeStretchMode = TimeStretchMode.Resample;
            else
                OnPropertyChanged();   // re-assert current value so ToggleButton stays in sync
        }
    }

    /// <summary>
    /// Helper bool for TwoWay ToggleButton binding: true when mode is <see cref="TimeStretchMode.TimeStretch"/>.
    /// Setting to false is silently ignored — use <see cref="IsResampleMode"/> to switch.
    /// </summary>
    public bool IsTimeStretchMode
    {
        get => _timeStretchMode == TimeStretchMode.TimeStretch;
        set
        {
            if (value)
                TimeStretchMode = TimeStretchMode.TimeStretch;
            else
                OnPropertyChanged();
        }
    }
    
    /// <summary>Time stretch ratio (0.5 = half speed, 2.0 = double speed).</summary>
    public double TimeStretchRatio
    {
        get => _timeStretchRatio;
        set => SetField(ref _timeStretchRatio, Math.Clamp(value, 0.25, 4.0));
    }
    
    /// <summary>Additional time multiplier.</summary>
    public double TimeStretchMultiplier
    {
        get => _timeStretchMultiplier;
        set => SetField(ref _timeStretchMultiplier, Math.Clamp(value, 0.25, 4.0));
    }
    
    /// <summary>Effective duration after time stretching.</summary>
    public TimeSpan EffectiveDuration => TimeSpan.FromTicks(
        (long)(OriginalDuration.Ticks / (TimeStretchRatio * TimeStretchMultiplier)));
    
    #endregion
    
    #region File Loading Options
    
    private bool _streamFromDisk;
    private bool _resampleOnLoad;
    private int _targetSampleRate = 44100;
    private bool _loadRegions;
    private bool _loadSliceMarkers;
    
    /// <summary>Stream audio from disk instead of loading into memory.</summary>
    public bool StreamFromDisk
    {
        get => _streamFromDisk;
        set => SetField(ref _streamFromDisk, value);
    }
    
    /// <summary>Resample audio on load to target sample rate.</summary>
    public bool ResampleOnLoad
    {
        get => _resampleOnLoad;
        set => SetField(ref _resampleOnLoad, value);
    }
    
    /// <summary>Target sample rate when resampling.</summary>
    public int TargetSampleRate
    {
        get => _targetSampleRate;
        set => SetField(ref _targetSampleRate, Math.Clamp(value, 8000, 192000));
    }
    
    /// <summary>Load region markers from file.</summary>
    public bool LoadRegions
    {
        get => _loadRegions;
        set => SetField(ref _loadRegions, value);
    }
    
    /// <summary>Load slice markers from file.</summary>
    public bool LoadSliceMarkers
    {
        get => _loadSliceMarkers;
        set => SetField(ref _loadSliceMarkers, value);
    }
    
    #endregion
    
    #region Waveform Data (for visualization)
    
    private float[]? _waveformPeaks;
    private bool _waveformGenerated;
    
    /// <summary>Pre-computed waveform peaks for visualization.</summary>
    public float[]? WaveformPeaks
    {
        get => _waveformPeaks;
        set
        {
            _waveformPeaks = value;
            _waveformGenerated = value != null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WaveformGenerated));
        }
    }
    
    /// <summary>Whether waveform data has been generated.</summary>
    public bool WaveformGenerated => _waveformGenerated;
    
    #endregion
    
    #region Offline Processing State
    
    private bool _isNormalized;
    private bool _dcOffsetRemoved;
    private bool _isReversed;
    private bool _polarityInverted;
    private StereoMode _stereoMode = StereoMode.Stereo;
    
    /// <summary>Whether normalize has been applied.</summary>
    public bool IsNormalized
    {
        get => _isNormalized;
        set => SetField(ref _isNormalized, value);
    }
    
    /// <summary>Whether DC offset has been removed.</summary>
    public bool DcOffsetRemoved
    {
        get => _dcOffsetRemoved;
        set => SetField(ref _dcOffsetRemoved, value);
    }
    
    /// <summary>Whether audio has been reversed.</summary>
    public bool IsReversed
    {
        get => _isReversed;
        set => SetField(ref _isReversed, value);
    }
    
    /// <summary>Whether polarity has been inverted.</summary>
    public bool PolarityInverted
    {
        get => _polarityInverted;
        set => SetField(ref _polarityInverted, value);
    }
    
    /// <summary>Stereo processing mode.</summary>
    public StereoMode StereoMode
    {
        get => _stereoMode;
        set => SetField(ref _stereoMode, value);
    }
    
    #endregion
    
    #region AI Analysis Data (Future Extension)
    
    private string? _detectedKey;
    private double? _detectedBpm;
    private string? _detectedGenre;
    private TimeSpan[]? _transientMarkers;
    private Dictionary<string, object>? _analysisMetadata;
    
    /// <summary>AI-detected musical key.</summary>
    public string? DetectedKey
    {
        get => _detectedKey;
        set => SetField(ref _detectedKey, value);
    }
    
    /// <summary>AI-detected tempo in BPM.</summary>
    public double? DetectedBpm
    {
        get => _detectedBpm;
        set => SetField(ref _detectedBpm, value);
    }
    
    /// <summary>AI-detected genre.</summary>
    public string? DetectedGenre
    {
        get => _detectedGenre;
        set => SetField(ref _detectedGenre, value);
    }
    
    /// <summary>AI-detected transient positions.</summary>
    public TimeSpan[]? TransientMarkers
    {
        get => _transientMarkers;
        set => SetField(ref _transientMarkers, value);
    }
    
    /// <summary>Additional AI analysis metadata.</summary>
    public Dictionary<string, object>? AnalysisMetadata
    {
        get => _analysisMetadata;
        set => SetField(ref _analysisMetadata, value);
    }
    
    #endregion
    
    #region INotifyPropertyChanged
    
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
    
    #endregion
}

/// <summary>
/// Time stretching algorithm modes.
/// </summary>
public enum TimeStretchMode
{
    /// <summary>Simple resampling (changes pitch with time).</summary>
    Resample,
    
    /// <summary>Time stretch without pitch change.</summary>
    TimeStretch,
    
    /// <summary>Granular time stretching for extreme ratios.</summary>
    Granular,
    
    /// <summary>Auto-select best algorithm.</summary>
    Auto
}

/// <summary>
/// Stereo processing modes.
/// </summary>
public enum StereoMode
{
    /// <summary>Keep original stereo.</summary>
    Stereo,
    
    /// <summary>Convert to mono (L+R).</summary>
    Mono,
    
    /// <summary>Use left channel only.</summary>
    LeftOnly,
    
    /// <summary>Use right channel only.</summary>
    RightOnly,
    
    /// <summary>Swap left and right channels.</summary>
    SwapChannels,
    
    /// <summary>Mid-side encoding.</summary>
    MidSide
}
