using System.Text.Json.Serialization;
using DAW.Models;

namespace DAW.Models;

/// <summary>
/// Represents a complete DAW project that can be saved to and loaded from JSON.
/// Contains all session data including tracks, clips, mixer settings, and global parameters.
/// File extension: .dawproj
/// </summary>
public sealed class DawProject
{
    /// <summary>
    /// Format version for backward compatibility
    /// </summary>
    public string FormatVersion { get; set; } = "2.0";
    
    /// <summary>
    /// Project metadata
    /// </summary>
    public string ProjectName { get; set; } = "Untitled Project";
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = Environment.UserName;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime LastModifiedDate { get; set; } = DateTime.Now;
    public string Version { get; set; } = "1.0";
    
    /// <summary>
    /// Full file path where this project is saved
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Project history and timeline
    /// </summary>
    public ProjectHistory History { get; set; } = new();

    /// <summary>
    /// Global project settings
    /// </summary>
    public ProjectSettings Settings { get; set; } = new();
    
    /// <summary>
    /// Export configuration for rendering
    /// </summary>
    public ExportSettings ExportSettings { get; set; } = new();

    /// <summary>
    /// All tracks in the project
    /// </summary>
    public List<ProjectTrack> Tracks { get; set; } = [];

    /// <summary>
    /// Master channel settings
    /// </summary>
    public MasterChannelData MasterChannel { get; set; } = new();

    /// <summary>
    /// Project-wide automation data
    /// </summary>
    public List<AutomationClip> Automation { get; set; } = [];

    /// <summary>
    /// Recently used file paths for quick access
    /// </summary>
    public List<string> RecentFiles { get; set; } = [];
    
    /// <summary>
    /// Referenced audio files used in this project
    /// </summary>
    public List<ProjectFileReference> Files { get; set; } = [];
}

/// <summary>
/// Project history tracking opens, saves, and changes
/// </summary>
public sealed class ProjectHistory
{
    /// <summary>
    /// When the project was first opened in this session
    /// </summary>
    public DateTime? SessionStartTime { get; set; }
    
    /// <summary>
    /// List of all times the project was opened
    /// </summary>
    public List<DateTime> OpenHistory { get; set; } = [];
    
    /// <summary>
    /// List of all times the project was saved
    /// </summary>
    public List<DateTime> SaveHistory { get; set; } = [];
    
    /// <summary>
    /// Detailed change log
    /// </summary>
    public List<ProjectChangeEntry> ChangeLog { get; set; } = [];
    
    /// <summary>
    /// Total editing time in minutes
    /// </summary>
    public double TotalEditingTimeMinutes { get; set; }
}

/// <summary>
/// Single change entry in the project history
/// </summary>
public sealed class ProjectChangeEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ChangeType { get; set; } = "Unknown"; // Created, Modified, TrackAdded, EffectChanged, etc.
    public string Description { get; set; } = string.Empty;
    public string? AffectedElement { get; set; } // e.g., "Track 1", "Master Compressor"
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>
/// Export/Render settings for the project
/// </summary>
public sealed class ExportSettings
{
    /// <summary>
    /// Default export format (WAV, MP3, FLAC, OGG)
    /// </summary>
    public string DefaultFormat { get; set; } = "WAV";
    
    /// <summary>
    /// Default export directory
    /// </summary>
    public string? ExportDirectory { get; set; }
    
    /// <summary>
    /// WAV export settings
    /// </summary>
    public WavExportSettings Wav { get; set; } = new();
    
    /// <summary>
    /// MP3 export settings
    /// </summary>
    public Mp3ExportSettings Mp3 { get; set; } = new();
    
    /// <summary>
    /// FLAC export settings
    /// </summary>
    public FlacExportSettings Flac { get; set; } = new();
    
    /// <summary>
    /// OGG export settings
    /// </summary>
    public OggExportSettings Ogg { get; set; } = new();
    
    /// <summary>
    /// Export range settings
    /// </summary>
    public ExportRangeSettings Range { get; set; } = new();
    
    /// <summary>
    /// Whether to normalize on export
    /// </summary>
    public bool NormalizeOnExport { get; set; } = false;
    
    /// <summary>
    /// Target normalization level in dB
    /// </summary>
    public double NormalizationTargetDb { get; set; } = -0.3;
    
    /// <summary>
    /// Whether to dither on export (when reducing bit depth)
    /// </summary>
    public bool DitherOnExport { get; set; } = true;
}

/// <summary>
/// WAV export configuration
/// </summary>
public sealed class WavExportSettings
{
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 24; // 16, 24, 32
    public int Channels { get; set; } = 2; // 1 = Mono, 2 = Stereo
}

/// <summary>
/// MP3 export configuration
/// </summary>
public sealed class Mp3ExportSettings
{
    public int SampleRate { get; set; } = 44100;
    public int Bitrate { get; set; } = 320; // kbps: 128, 192, 256, 320
    public string BitrateMode { get; set; } = "CBR"; // CBR, VBR
    public int VbrQuality { get; set; } = 0; // 0 (best) to 9 (worst) for VBR
    public bool WriteId3Tags { get; set; } = true;
}

/// <summary>
/// FLAC export configuration
/// </summary>
public sealed class FlacExportSettings
{
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 24;
    public int CompressionLevel { get; set; } = 5; // 0-8, higher = smaller file
}

/// <summary>
/// OGG Vorbis export configuration
/// </summary>
public sealed class OggExportSettings
{
    public int SampleRate { get; set; } = 44100;
    public double Quality { get; set; } = 0.8; // 0.0 to 1.0
}

/// <summary>
/// Export range configuration
/// </summary>
public sealed class ExportRangeSettings
{
    public string Mode { get; set; } = "Full"; // Full, Selection, Loop, Custom
    public double StartBeat { get; set; } = 0;
    public double EndBeat { get; set; } = 64;
    public bool IncludeTailSilence { get; set; } = true;
    public double TailSilenceSeconds { get; set; } = 2.0;
}

/// <summary>
/// Reference to an audio file used in the project
/// </summary>
public sealed class ProjectFileReference
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalPath { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? FileHash { get; set; } // For integrity checking
    public DateTime? LastModified { get; set; }
    public TimeSpan? Duration { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
}

/// <summary>
/// Global project settings and preferences
/// </summary>
public sealed class ProjectSettings
{
    public double BPM { get; set; } = 140.0;
    public int TimeSignatureNumerator { get; set; } = 4;
    public int TimeSignatureDenominator { get; set; } = 4;
    public string Key { get; set; } = "C Major";
    public double ProjectLength { get; set; } = 64.0; // In bars
    
    // Playback settings
    public TimeSpan PlaybackPosition { get; set; } = TimeSpan.Zero;
    public bool LoopEnabled { get; set; } = false;
    public double LoopStart { get; set; } = 0.0;
    public double LoopEnd { get; set; } = 16.0;
    
    // Audio settings
    public int SampleRate { get; set; } = 44100;
    public int BufferSize { get; set; } = 512;
    public int BitDepth { get; set; } = 24;
    
    // Global tool state
    public string ActiveTool { get; set; } = "Select";
    public bool MetronomeEnabled { get; set; } = false;
    public bool MonitoringEnabled { get; set; } = true;
    public double SnapResolution { get; set; } = 1.0;
}

/// <summary>
/// Represents a track with all its properties and clips
/// </summary>
public sealed class ProjectTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int TrackNumber { get; set; }
    public string Title { get; set; } = "Track";
    public string Artist { get; set; } = "Unknown";
    public string FilePath { get; set; } = string.Empty;
    
    // Visual properties
    public ProjectColor Color { get; set; } = new(38, 97, 156);
    
    // Audio properties
    public double Volume { get; set; } = 0.8;
    public double Pan { get; set; } = 0.0;
    public bool IsMuted { get; set; } = false;
    public bool IsSolo { get; set; } = false;
    public bool IsArmed { get; set; } = false;
    
    // Clips on this track
    public List<ProjectClip> Clips { get; set; } = [];
    
    // Effects chain
    public List<ProjectEffect> Effects { get; set; } = [];
    
    // Send levels (to reverb, delay, etc.)
    public Dictionary<string, double> SendLevels { get; set; } = [];
}

/// <summary>
/// Represents a clip (audio or MIDI) on the timeline
/// </summary>
public sealed class ProjectClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = "Clip";
    public double StartBeat { get; set; }
    public double LengthInBeats { get; set; } = 4.0;
    public string? SourceFilePath { get; set; }
    public ProjectColor Color { get; set; } = new(38, 97, 156);
    public bool IsSelected { get; set; } = false;
    public bool IsMuted { get; set; } = false;
    
    // Audio-specific properties
    public TimeSpan? AudioDuration { get; set; }
    public double[] WaveformData { get; set; } = [];
    public bool IsAudioClip => WaveformData.Length > 0 || !string.IsNullOrEmpty(SourceFilePath);
    
    // Clip-specific settings
    public double Volume { get; set; } = 1.0;
    public double Pan { get; set; } = 0.0;
    public double Pitch { get; set; } = 0.0; // In semitones
    public double TimeStretch { get; set; } = 1.0;
    
    // Fade in/out
    public double FadeInLength { get; set; } = 0.0;
    public double FadeOutLength { get; set; } = 0.0;
}

/// <summary>
/// Represents an effect/plugin with its parameters
/// </summary>
public sealed class ProjectEffect
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Effect";
    public string Type { get; set; } = "Unknown"; // Equalizer, Compressor, Reverb, Delay, Gain, etc.
    public string Icon { get; set; } = "🎛️";
    public bool IsEnabled { get; set; } = true;
    public bool IsExpanded { get; set; } = false;
    public int SlotIndex { get; set; } = 0;
    
    /// <summary>
    /// Effect parameters stored by name
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = [];
    
    /// <summary>
    /// Preset name if using a preset
    /// </summary>
    public string? PresetName { get; set; }
    
    /// <summary>
    /// Effect-specific detailed parameters for built-in effects
    /// </summary>
    public EqualizerParameters? Equalizer { get; set; }
    public CompressorParameters? Compressor { get; set; }
    public ReverbParameters? Reverb { get; set; }
    public DelayParameters? Delay { get; set; }
    public GainParameters? Gain { get; set; }
}

/// <summary>
/// 7-Band Parametric EQ parameters (FL-style).
/// </summary>
public sealed class EqualizerParameters
{
    // Legacy properties (backward compat)
    public double LowGain { get; set; }
    public double MidGain { get; set; }
    public double HighGain { get; set; }
    public double LowFrequency { get; set; } = 200.0;
    public double HighFrequency { get; set; } = 4000.0;
    public double MidQ { get; set; } = 1.0;

    // Full 7-band data
    public List<EqBandParameters> Bands { get; set; } = [];
}

public sealed class EqBandParameters
{
    public int Number { get; set; }
    public double Gain { get; set; }
    public double Frequency { get; set; }
    public double Q { get; set; } = 1.0;
    public int Mode { get; set; } // EqBandMode enum as int
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Compressor parameters
/// </summary>
public sealed class CompressorParameters
{
    public double Threshold { get; set; } = -20.0; // dB, -60 to 0
    public double Ratio { get; set; } = 4.0; // 1:1 to 20:1
    public double Attack { get; set; } = 10.0; // ms, 0.1 to 100
    public double Release { get; set; } = 100.0; // ms, 10 to 1000
    public double MakeupGain { get; set; } = 0.0; // dB, 0 to 24
    public double Knee { get; set; } = 6.0; // dB, 0 to 12
}

/// <summary>
/// Reverb parameters
/// </summary>
public sealed class ReverbParameters
{
    public double RoomSize { get; set; } = 0.5; // 0 to 1
    public double Damping { get; set; } = 0.5;
    public double WetLevel { get; set; } = 0.3;
    public double DryLevel { get; set; } = 0.7;
    public double PreDelay { get; set; } = 20.0; // ms
    public double Width { get; set; } = 1.0;
}

/// <summary>
/// Delay parameters
/// </summary>
public sealed class DelayParameters
{
    public double DelayTime { get; set; } = 250.0; // ms, 1 to 2000
    public double Feedback { get; set; } = 0.3; // 0 to 0.95
    public double WetMix { get; set; } = 0.3; // 0 to 1
    public double HighCut { get; set; } = 8000.0; // Hz
    public double LowCut { get; set; } = 100.0;
    public bool SyncToTempo { get; set; } = false;
    public string SyncDivision { get; set; } = "1/4"; // 1/4, 1/8, 1/16, etc.
}

/// <summary>
/// Gain parameters
/// </summary>
public sealed class GainParameters
{
    public double GainDb { get; set; } = 0.0; // dB, -24 to +24
}

/// <summary>
/// Master channel settings with full mixer data
/// </summary>
public sealed class MasterChannelData
{
    public double Volume { get; set; } = 0.8;
    public double Pan { get; set; } = 0.0;
    public double VolumeDb => Volume > 0 ? 20 * Math.Log10(Volume) : -100;
    
    /// <summary>
    /// Effects on the master channel
    /// </summary>
    public List<ProjectEffect> Effects { get; set; } = [];
    
    /// <summary>
    /// Whether the master limiter is enabled
    /// </summary>
    public bool IsLimiterEnabled { get; set; } = true;
    
    /// <summary>
    /// Master limiter settings
    /// </summary>
    public LimiterParameters Limiter { get; set; } = new();
    
    /// <summary>
    /// Peak levels for metering (not saved, but placeholder for runtime)
    /// </summary>
    public double PeakLevelL { get; set; } = 0.0;
    public double PeakLevelR { get; set; } = 0.0;
}

/// <summary>
/// Limiter parameters for master channel
/// </summary>
public sealed class LimiterParameters
{
    public double Ceiling { get; set; } = -0.3; // dB
    public double Release { get; set; } = 50.0; // ms
    public bool AutoMakeup { get; set; } = true;
}

/// <summary>
/// Automation clip for parameter automation
/// </summary>
public sealed class AutomationClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TargetParameter { get; set; } = string.Empty; // e.g., "Track1.Volume"
    public double StartBeat { get; set; }
    public double LengthInBeats { get; set; } = 4.0;
    public List<AutomationPoint> Points { get; set; } = [];
}

/// <summary>
/// Single automation point
/// </summary>
public sealed class AutomationPoint
{
    public double Position { get; set; } // 0.0 to 1.0 within the clip
    public double Value { get; set; }
    public string CurveType { get; set; } = "Linear"; // Linear, Smooth, Hold, etc.
}

/// <summary>
/// Color representation for JSON serialization
/// </summary>
public sealed class ProjectColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public ProjectColor() { }

    public ProjectColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public ProjectColor(System.Windows.Media.Color color)
    {
        R = color.R;
        G = color.G;
        B = color.B;
    }

    public System.Windows.Media.Color ToMediaColor()
    {
        return System.Windows.Media.Color.FromRgb(R, G, B);
    }

    public static implicit operator ProjectColor(System.Windows.Media.Color color)
    {
        return new ProjectColor(color);
    }

    public static implicit operator System.Windows.Media.Color(ProjectColor color)
    {
        return color.ToMediaColor();
    }
}