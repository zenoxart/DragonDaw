using DAW.Models;

namespace DAW.Tests;

/// <summary>
/// Unit tests for ClipData model - testing real-time parameter handling.
/// </summary>
public class ClipDataTests
{
    [Fact]
    public void Volume_ShouldClampBetweenZeroAndTwo()
    {
        var clip = new ClipData();
        
        clip.Volume = 1.5f;
        Assert.Equal(1.5f, clip.Volume);
        
        clip.Volume = -1f;
        Assert.Equal(0f, clip.Volume);
        
        clip.Volume = 3f;
        Assert.Equal(2f, clip.Volume);
    }
    
    [Fact]
    public void Pan_ShouldClampBetweenMinusOneAndOne()
    {
        var clip = new ClipData();
        
        clip.Pan = 0.5f;
        Assert.Equal(0.5f, clip.Pan);
        
        clip.Pan = -2f;
        Assert.Equal(-1f, clip.Pan);
        
        clip.Pan = 2f;
        Assert.Equal(1f, clip.Pan);
    }
    
    [Fact]
    public void PitchSemitones_ShouldClampBetweenMinus24And24()
    {
        var clip = new ClipData();
        
        clip.PitchSemitones = 12f;
        Assert.Equal(12f, clip.PitchSemitones);
        
        clip.PitchSemitones = -30f;
        Assert.Equal(-24f, clip.PitchSemitones);
        
        clip.PitchSemitones = 30f;
        Assert.Equal(24f, clip.PitchSemitones);
    }
    
    [Fact]
    public void VolumeDb_ShouldReturnCorrectDecibels()
    {
        var clip = new ClipData();
        
        clip.Volume = 1.0f;
        Assert.Contains("0", clip.VolumeDb); // 0.0 or 0,0 depending on culture
        Assert.Contains("dB", clip.VolumeDb);
        
        clip.Volume = 0f;
        Assert.Equal("-∞ dB", clip.VolumeDb);
    }
    
    [Fact]
    public void PanDisplay_ShouldReturnCorrectLabel()
    {
        var clip = new ClipData();
        
        clip.Pan = 0f;
        Assert.Equal("C", clip.PanDisplay);
        
        clip.Pan = -0.5f;
        Assert.Equal("L 50", clip.PanDisplay);
        
        clip.Pan = 0.5f;
        Assert.Equal("R 50", clip.PanDisplay);
    }
    
    [Fact]
    public void IsEnabled_ShouldDefaultToTrue()
    {
        var clip = new ClipData();
        Assert.True(clip.IsEnabled);
    }
    
    [Fact]
    public void LoopPoints_ShouldNotBeNegative()
    {
        var clip = new ClipData();
        
        clip.LoopStartSamples = -100;
        Assert.Equal(0, clip.LoopStartSamples);
        
        clip.LoopEndSamples = -100;
        Assert.Equal(0, clip.LoopEndSamples);
    }
    
    [Fact]
    public void TimeStretchRatio_ShouldClamp()
    {
        var clip = new ClipData();
        
        clip.TimeStretchRatio = 2.0;
        Assert.Equal(2.0, clip.TimeStretchRatio);
        
        clip.TimeStretchRatio = 0.1;
        Assert.Equal(0.25, clip.TimeStretchRatio);
        
        clip.TimeStretchRatio = 10.0;
        Assert.Equal(4.0, clip.TimeStretchRatio);
    }
    
    [Fact]
    public void StartOffset_ShouldCalculateCorrectTimeSpan()
    {
        var clip = new ClipData
        {
            SampleRate = 44100,
            StartOffsetSamples = 44100 // 1 second
        };
        
        Assert.Equal(TimeSpan.FromSeconds(1), clip.StartOffset);
    }
    
    [Fact]
    public void EffectiveDuration_ShouldAccountForTimeStretch()
    {
        var clip = new ClipData
        {
            OriginalDuration = TimeSpan.FromSeconds(10),
            TimeStretchRatio = 2.0,
            TimeStretchMultiplier = 1.0
        };
        
        // At 2x speed, 10 seconds becomes 5 seconds
        Assert.Equal(TimeSpan.FromSeconds(5), clip.EffectiveDuration);
    }
    
    [Fact]
    public void PropertyChanged_ShouldFireOnVolumeChange()
    {
        var clip = new ClipData();
        var propertyChangedFired = false;
        
        clip.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClipData.Volume) || e.PropertyName == nameof(ClipData.VolumeDb))
                propertyChangedFired = true;
        };
        
        clip.Volume = 0.5f;
        Assert.True(propertyChangedFired);
    }
}
