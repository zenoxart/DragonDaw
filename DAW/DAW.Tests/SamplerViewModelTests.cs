using DAW.Models;
using DAW.ViewModels;

namespace DAW.Tests;

/// <summary>
/// Unit tests for SamplerViewModel.
/// </summary>
public class SamplerViewModelTests
{
    private Track CreateTestTrack()
    {
        return new Track
        {
            Title = "Test Track",
            FilePath = "", // Empty path for testing without actual file
            Duration = TimeSpan.FromSeconds(60)
        };
    }
    
    [Fact]
    public void Constructor_ShouldInitializeClipData()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        Assert.NotNull(vm.ClipData);
        Assert.Equal("Test Track", vm.ClipData.DisplayName);
    }
    
    [Fact]
    public void WindowTitle_ShouldIncludeTrackName()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        Assert.Contains("Test Track", vm.WindowTitle);
    }
    
    [Fact]
    public void ZoomLevel_ShouldClamp()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.ZoomLevel = 50;
        Assert.Equal(50, vm.ZoomLevel);
        
        vm.ZoomLevel = 0.05;
        Assert.Equal(0.1, vm.ZoomLevel);
        
        vm.ZoomLevel = 200;
        Assert.Equal(100, vm.ZoomLevel);
    }
    
    [Fact]
    public void ScrollPosition_ShouldClampBetweenZeroAndOne()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.ScrollPosition = 0.5;
        Assert.Equal(0.5, vm.ScrollPosition);
        
        vm.ScrollPosition = -1;
        Assert.Equal(0, vm.ScrollPosition);
        
        vm.ScrollPosition = 2;
        Assert.Equal(1, vm.ScrollPosition);
    }
    
    [Fact]
    public void SelectionState_ShouldUpdateHasSelection()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.SelectionStart = 0;
        vm.SelectionEnd = 0;
        Assert.False(vm.HasSelection);
        
        vm.SelectionStart = 0;
        vm.SelectionEnd = 1000;
        Assert.True(vm.HasSelection);
    }
    
    [Fact]
    public void Commands_ShouldBeInitialized()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        Assert.NotNull(vm.LoadFileCommand);
        Assert.NotNull(vm.PlayCommand);
        Assert.NotNull(vm.StopCommand);
        Assert.NotNull(vm.ZoomInCommand);
        Assert.NotNull(vm.ZoomOutCommand);
        Assert.NotNull(vm.NormalizeCommand);
        Assert.NotNull(vm.ReverseCommand);
    }
    
    [Fact]
    public void ZoomInCommand_ShouldIncreaseZoomLevel()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        var initialZoom = vm.ZoomLevel;
        vm.ZoomInCommand.Execute(null);
        
        Assert.True(vm.ZoomLevel > initialZoom);
    }
    
    [Fact]
    public void ZoomOutCommand_ShouldDecreaseZoomLevel()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.ZoomLevel = 10;
        var initialZoom = vm.ZoomLevel;
        vm.ZoomOutCommand.Execute(null);
        
        Assert.True(vm.ZoomLevel < initialZoom);
    }
    
    [Fact]
    public void ZoomToFitCommand_ShouldResetZoomLevel()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.ZoomLevel = 5;
        vm.ZoomToFitCommand.Execute(null);
        
        Assert.Equal(1.0, vm.ZoomLevel);
    }
    
    [Fact]
    public void PlayheadDisplay_ShouldFormatCorrectly()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.PlayheadPosition = TimeSpan.FromSeconds(65.5);
        
        Assert.Equal("01:05.500", vm.PlayheadDisplay);
    }
    
    [Fact]
    public void SelectAllCommand_ShouldSelectEntireClip()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.TotalSamples = 44100 * 60; // 60 seconds at 44.1kHz
        
        vm.SelectAllCommand.Execute(null);
        
        Assert.Equal(0, vm.SelectionStart);
        Assert.Equal(vm.ClipData.TotalSamples, vm.SelectionEnd);
        Assert.True(vm.HasSelection);
    }
    
    [Fact]
    public void ClearSelectionCommand_ShouldClearSelection()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        vm.SelectionStart = 0;
        vm.SelectionEnd = 1000;
        
        vm.ClearSelectionCommand.Execute(null);
        
        Assert.Equal(0, vm.SelectionStart);
        Assert.Equal(0, vm.SelectionEnd);
        Assert.False(vm.HasSelection);
    }
    
    [Fact]
    public void TimeStretchModes_ShouldContainAllValues()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        Assert.Contains(TimeStretchMode.Resample, vm.TimeStretchModes);
        Assert.Contains(TimeStretchMode.TimeStretch, vm.TimeStretchModes);
        Assert.Contains(TimeStretchMode.Granular, vm.TimeStretchModes);
        Assert.Contains(TimeStretchMode.Auto, vm.TimeStretchModes);
    }
    
    [Fact]
    public void IsProcessing_ShouldRaisePropertyChanged()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        var propertyChanged = false;
        
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsProcessing))
                propertyChanged = true;
        };
        
        vm.IsProcessing = true;
        
        Assert.True(propertyChanged);
    }
    
    [Fact]
    public void StatusMessage_ShouldHaveInitialValue()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        // Should have some status message (varies based on whether file is loaded)
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }
    
    [Fact]
    public void SelectionStartNormalized_ShouldCalculateCorrectly()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.TotalSamples = 1000;
        
        vm.SelectionStart = 500;
        
        Assert.Equal(0.5, vm.SelectionStartNormalized, 2);
    }
    
    [Fact]
    public void SelectionEndNormalized_ShouldCalculateCorrectly()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.TotalSamples = 1000;
        
        vm.SelectionEnd = 750;
        
        Assert.Equal(0.75, vm.SelectionEndNormalized, 2);
    }
    
    [Fact]
    public void LoopStartNormalized_ShouldCalculateCorrectly()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.TotalSamples = 2000;
        vm.ClipData.LoopStartSamples = 500;
        
        Assert.Equal(0.25, vm.LoopStartNormalized, 2);
    }
    
    [Fact]
    public void LoopEndNormalized_ShouldCalculateCorrectly()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.TotalSamples = 2000;
        vm.ClipData.LoopEndSamples = 1500;
        
        Assert.Equal(0.75, vm.LoopEndNormalized, 2);
    }
    
    [Fact]
    public void SetLoopFromSelectionCommand_ShouldSetLoopPoints()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.TotalSamples = 10000;
        
        vm.SelectionStart = 1000;
        vm.SelectionEnd = 5000;
        
        vm.SetLoopFromSelectionCommand.Execute(null);
        
        Assert.Equal(1000, vm.ClipData.LoopStartSamples);
        Assert.Equal(5000, vm.ClipData.LoopEndSamples);
        Assert.True(vm.ClipData.LoopEnabled);
    }
    
    [Fact]
    public void ClearLoopCommand_ShouldDisableLoop()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        vm.ClipData.LoopEnabled = true;
        vm.ClipData.LoopStartSamples = 500;
        
        vm.ClearLoopCommand.Execute(null);
        
        Assert.False(vm.ClipData.LoopEnabled);
        Assert.Equal(0, vm.ClipData.LoopStartSamples);
    }
    
    [Fact]
    public void Track_ShouldBeAccessible()
    {
        var track = CreateTestTrack();
        var vm = new SamplerViewModel(track);
        
        Assert.Same(track, vm.Track);
    }
}
