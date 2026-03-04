using DAW.Models;
using DAW.Services;

namespace DAW.Tests;

/// <summary>
/// Unit tests for OfflineProcessingService.
/// </summary>
public class OfflineProcessingServiceTests
{
    [Fact]
    public async Task QueueJobAsync_ShouldCompleteSuccessfully()
    {
        var service = OfflineProcessingService.Instance;
        var clip = new ClipData { DisplayName = "Test" };
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.InvertPolarity,
            TargetClip = clip
        };
        
        var completedJob = await service.QueueJobAsync(job);
        
        Assert.True(completedJob.IsComplete);
        Assert.False(completedJob.IsCancelled);
    }
    
    [Fact]
    public async Task QueueJobAsync_ShouldReportProgress()
    {
        var service = OfflineProcessingService.Instance;
        var clip = new ClipData { DisplayName = "Test" };
        var progressReported = false;
        
        service.JobProgress += (_, j) => 
        {
            if (j.TargetClip == clip)
                progressReported = true;
        };
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.Reverse,
            TargetClip = clip
        };
        
        await service.QueueJobAsync(job);
        
        Assert.True(progressReported);
    }
    
    [Fact]
    public async Task QueueJobAsync_ShouldToggleClipState()
    {
        var service = OfflineProcessingService.Instance;
        var clip = new ClipData 
        { 
            DisplayName = "Test",
            IsReversed = false 
        };
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.Reverse,
            TargetClip = clip
        };
        
        await service.QueueJobAsync(job);
        
        Assert.True(clip.IsReversed);
    }
    
    [Fact]
    public async Task QueueJobAsync_ShouldTogglePolarityState()
    {
        var service = OfflineProcessingService.Instance;
        var clip = new ClipData 
        { 
            DisplayName = "Test",
            PolarityInverted = false 
        };
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.InvertPolarity,
            TargetClip = clip
        };
        
        await service.QueueJobAsync(job);
        
        Assert.True(clip.PolarityInverted);
    }
    
    [Fact]
    public void CancelJobsForClip_ShouldCancelCorrectJobs()
    {
        var service = OfflineProcessingService.Instance;
        var clip1 = new ClipData { DisplayName = "Clip1" };
        var clip2 = new ClipData { DisplayName = "Clip2" };
        
        // This test verifies cancel doesn't throw
        service.CancelJobsForClip(clip1);
        service.CancelJobsForClip(clip2);
    }
    
    [Fact]
    public async Task QueueJobAsync_ShouldSetCompletedAt()
    {
        var service = OfflineProcessingService.Instance;
        var clip = new ClipData { DisplayName = "Test" };
        
        var job = new OfflineProcessingJob
        {
            ProcessType = OfflineProcessType.InvertPolarity,
            TargetClip = clip
        };
        
        var before = DateTime.UtcNow;
        var completedJob = await service.QueueJobAsync(job);
        var after = DateTime.UtcNow;
        
        Assert.NotNull(completedJob.CompletedAt);
        Assert.True(completedJob.CompletedAt >= before);
        Assert.True(completedJob.CompletedAt <= after);
    }
}
