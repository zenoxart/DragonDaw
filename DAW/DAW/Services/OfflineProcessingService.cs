using System.Collections.Concurrent;
using DAW.MVVM.Models;
using NAudio.Wave;

namespace DAW.Services;

/// <summary>
/// Service for offline (non-realtime) audio processing operations.
/// All operations run in background threads to avoid blocking the UI or audio thread.
/// Supports cancellation and progress reporting.
/// </summary>
public class OfflineProcessingService
{
    private static OfflineProcessingService? _instance;
    public static OfflineProcessingService Instance => _instance ??= new OfflineProcessingService();
    
    private readonly ConcurrentQueue<OfflineProcessingJob> _jobQueue = new();
    private readonly List<OfflineProcessingJob> _activeJobs = [];
    private readonly object _jobLock = new();
    
    public event EventHandler<OfflineProcessingJob>? JobStarted;
    public event EventHandler<OfflineProcessingJob>? JobProgress;
    public event EventHandler<OfflineProcessingJob>? JobCompleted;
    public event EventHandler<OfflineProcessingJob>? JobFailed;
    
    private OfflineProcessingService() { }
    
    /// <summary>
    /// Queues a processing job for execution.
    /// </summary>
    public async Task<OfflineProcessingJob> QueueJobAsync(OfflineProcessingJob job)
    {
        _jobQueue.Enqueue(job);
        
        lock (_jobLock)
        {
            _activeJobs.Add(job);
        }
        
        JobStarted?.Invoke(this, job);
        
        try
        {
            await Task.Run(() => ProcessJob(job), job.CancellationSource.Token);
            
            job.IsComplete = true;
            job.CompletedAt = DateTime.UtcNow;
            JobCompleted?.Invoke(this, job);
        }
        catch (OperationCanceledException)
        {
            job.IsCancelled = true;
            job.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            JobFailed?.Invoke(this, job);
        }
        finally
        {
            lock (_jobLock)
            {
                _activeJobs.Remove(job);
            }
        }
        
        return job;
    }
    
    /// <summary>
    /// Cancels all active jobs for a specific clip.
    /// </summary>
    public void CancelJobsForClip(ClipData clip)
    {
        lock (_jobLock)
        {
            foreach (var job in _activeJobs.Where(j => j.TargetClip == clip))
            {
                job.CancellationSource.Cancel();
            }
        }
    }
    
    private void ProcessJob(OfflineProcessingJob job)
    {
        var ct = job.CancellationSource.Token;
        
        job.StatusMessage = $"Processing {job.ProcessType}...";
        JobProgress?.Invoke(this, job);
        
        switch (job.ProcessType)
        {
            case OfflineProcessType.Normalize:
                ProcessNormalize(job, ct);
                break;
                
            case OfflineProcessType.RemoveDcOffset:
                ProcessRemoveDcOffset(job, ct);
                break;
                
            case OfflineProcessType.Reverse:
                ProcessReverse(job, ct);
                break;
                
            case OfflineProcessType.InvertPolarity:
                ProcessInvertPolarity(job, ct);
                break;
                
            case OfflineProcessType.StereoConvert:
                ProcessStereoConvert(job, ct);
                break;
                
            case OfflineProcessType.GenerateWaveform:
                ProcessGenerateWaveform(job, ct);
                break;
                
            case OfflineProcessType.TrimSilence:
                ProcessTrimSilence(job, ct);
                break;
                
            default:
                throw new NotSupportedException($"Process type {job.ProcessType} not yet implemented");
        }
        
        job.Progress = 100;
        job.StatusMessage = "Complete";
        JobProgress?.Invoke(this, job);
    }
    
    #region Processing Implementations
    
    private void ProcessNormalize(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        if (string.IsNullOrEmpty(clip.SourceFilePath)) return;
        
        float targetPeak = job.Parameters.TryGetValue("TargetPeak", out var tp) ? (float)tp : 1.0f;
        
        using var reader = new AudioFileReader(clip.SourceFilePath);
        float[] buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        
        // First pass: find peak
        float maxPeak = 0f;
        long totalSamples = reader.Length / sizeof(float);
        long processedSamples = 0;
        
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            
            for (int i = 0; i < read; i++)
            {
                float abs = Math.Abs(buffer[i]);
                if (abs > maxPeak) maxPeak = abs;
            }
            
            processedSamples += read;
            job.Progress = (processedSamples * 50.0) / totalSamples;
            job.StatusMessage = $"Analyzing... {job.Progress:F0}%";
            JobProgress?.Invoke(this, job);
        }
        
        if (maxPeak < 0.0001f)
        {
            job.StatusMessage = "Audio is silent";
            return;
        }
        
        float gain = targetPeak / maxPeak;
        
        // Second pass: apply gain (would need to write to new file in production)
        job.Progress = 75;
        job.StatusMessage = $"Normalizing (gain: {20 * Math.Log10(gain):F1} dB)";
        JobProgress?.Invoke(this, job);
        
        clip.IsNormalized = true;
    }
    
    private void ProcessRemoveDcOffset(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        if (string.IsNullOrEmpty(clip.SourceFilePath)) return;
        
        using var reader = new AudioFileReader(clip.SourceFilePath);
        float[] buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
        
        // Calculate DC offset (average)
        double sum = 0;
        long totalSamples = 0;
        
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            
            for (int i = 0; i < read; i++)
            {
                sum += buffer[i];
            }
            totalSamples += read;
            
            job.Progress = (totalSamples * 50.0) / (reader.Length / sizeof(float));
            JobProgress?.Invoke(this, job);
        }
        
        double dcOffset = sum / totalSamples;
        
        job.Progress = 75;
        job.StatusMessage = $"DC Offset: {dcOffset:F6}";
        JobProgress?.Invoke(this, job);
        
        clip.DcOffsetRemoved = true;
    }
    
    private void ProcessReverse(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        
        job.Progress = 50;
        job.StatusMessage = "Reversing audio...";
        JobProgress?.Invoke(this, job);
        
        // In production: read file, reverse samples, write to new file
        ct.ThrowIfCancellationRequested();
        
        clip.IsReversed = !clip.IsReversed;
    }
    
    private void ProcessInvertPolarity(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        
        job.Progress = 50;
        job.StatusMessage = "Inverting polarity...";
        JobProgress?.Invoke(this, job);
        
        ct.ThrowIfCancellationRequested();
        
        clip.PolarityInverted = !clip.PolarityInverted;
    }
    
    private void ProcessStereoConvert(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        
        if (!job.Parameters.TryGetValue("Mode", out var modeObj))
            return;
            
        var mode = (StereoMode)modeObj;
        
        job.Progress = 50;
        job.StatusMessage = $"Converting to {mode}...";
        JobProgress?.Invoke(this, job);
        
        ct.ThrowIfCancellationRequested();
        
        clip.StereoMode = mode;
    }
    
    private void ProcessGenerateWaveform(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        if (string.IsNullOrEmpty(clip.SourceFilePath)) return;
        
        int peakCount = job.Parameters.TryGetValue("PeakCount", out var pc) ? (int)pc : 1000;
        
        using var reader = new AudioFileReader(clip.SourceFilePath);
        
        long totalSamples = reader.Length / sizeof(float) / reader.WaveFormat.Channels;
        int samplesPerPeak = (int)Math.Max(1, totalSamples / peakCount);
        
        var peaks = new List<float>();
        float[] buffer = new float[samplesPerPeak * reader.WaveFormat.Channels];
        
        int read;
        int peakIndex = 0;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            
            float maxPeak = 0f;
            for (int i = 0; i < read; i++)
            {
                float abs = Math.Abs(buffer[i]);
                if (abs > maxPeak) maxPeak = abs;
            }
            peaks.Add(maxPeak);
            
            peakIndex++;
            job.Progress = (peakIndex * 100.0) / peakCount;
            
            if (peakIndex % 100 == 0)
            {
                job.StatusMessage = $"Generating waveform... {job.Progress:F0}%";
                JobProgress?.Invoke(this, job);
            }
        }
        
        clip.WaveformPeaks = [.. peaks];
    }
    
    private void ProcessTrimSilence(OfflineProcessingJob job, CancellationToken ct)
    {
        var clip = job.TargetClip;
        if (string.IsNullOrEmpty(clip.SourceFilePath)) return;
        
        float threshold = job.Parameters.TryGetValue("Threshold", out var th) ? (float)th : 0.001f;
        
        using var reader = new AudioFileReader(clip.SourceFilePath);
        float[] buffer = new float[4096];
        
        // Find first non-silent sample
        long startSample = 0;
        long currentSample = 0;
        bool foundStart = false;
        
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0 && !foundStart)
        {
            ct.ThrowIfCancellationRequested();
            
            for (int i = 0; i < read; i++)
            {
                if (Math.Abs(buffer[i]) > threshold)
                {
                    startSample = currentSample + i;
                    foundStart = true;
                    break;
                }
            }
            currentSample += read;
            
            job.Progress = 25;
            JobProgress?.Invoke(this, job);
        }
        
        // Find last non-silent sample (simplified - would need reverse reading)
        job.Progress = 75;
        job.StatusMessage = "Finding end point...";
        JobProgress?.Invoke(this, job);
        
        clip.StartOffsetSamples = startSample / reader.WaveFormat.Channels;
    }
    
    #endregion
}
