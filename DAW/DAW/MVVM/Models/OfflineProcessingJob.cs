namespace DAW.MVVM.Models;

/// <summary>
/// Represents an offline processing job for the clip editor.
/// These operations are computed in a background thread, not in the audio thread.
/// </summary>
public class OfflineProcessingJob
{
    /// <summary>Unique identifier for this job.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    
    /// <summary>Type of processing operation.</summary>
    public required OfflineProcessType ProcessType { get; init; }
    
    /// <summary>Target clip data to process.</summary>
    public required ClipData TargetClip { get; init; }
    
    /// <summary>Processing parameters.</summary>
    public Dictionary<string, object> Parameters { get; init; } = [];
    
    /// <summary>Current progress (0-100).</summary>
    public double Progress { get; set; }
    
    /// <summary>Current status message.</summary>
    public string StatusMessage { get; set; } = string.Empty;
    
    /// <summary>Whether the job is complete.</summary>
    public bool IsComplete { get; set; }
    
    /// <summary>Whether the job was cancelled.</summary>
    public bool IsCancelled { get; set; }
    
    /// <summary>Error message if processing failed.</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>Cancellation token source for this job.</summary>
    public CancellationTokenSource CancellationSource { get; } = new();
    
    /// <summary>Time when job was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Time when job completed.</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Types of offline processing operations.
/// </summary>
public enum OfflineProcessType
{
    /// <summary>Normalize audio to peak level.</summary>
    Normalize,
    
    /// <summary>Normalize to RMS/LUFS level.</summary>
    NormalizeLoudness,
    
    /// <summary>Remove DC offset.</summary>
    RemoveDcOffset,
    
    /// <summary>Reverse audio.</summary>
    Reverse,
    
    /// <summary>Invert polarity (phase flip).</summary>
    InvertPolarity,
    
    /// <summary>Convert stereo mode.</summary>
    StereoConvert,
    
    /// <summary>Apply fade in.</summary>
    FadeIn,
    
    /// <summary>Apply fade out.</summary>
    FadeOut,
    
    /// <summary>Apply crossfade.</summary>
    Crossfade,
    
    /// <summary>Trim silence from start/end.</summary>
    TrimSilence,
    
    /// <summary>Time stretch (offline render).</summary>
    TimeStretch,
    
    /// <summary>Pitch shift (offline render).</summary>
    PitchShift,
    
    /// <summary>Generate waveform visualization.</summary>
    GenerateWaveform,
    
    /// <summary>AI analysis (tempo, key, transients).</summary>
    AiAnalysis,
    
    /// <summary>Detect transients/slices.</summary>
    DetectTransients,
    
    /// <summary>Apply destructive effects.</summary>
    ApplyEffects
}
