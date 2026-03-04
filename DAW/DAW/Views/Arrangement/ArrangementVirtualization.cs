using System.Windows;
using System.Windows.Controls;

namespace DAW.Views.Arrangement;

/// <summary>
/// Enhanced virtualization strategy for handling 100+ tracks with thousands of clips.
/// Provides viewport-based culling and optimized rendering.
/// </summary>
public static class ArrangementVirtualization
{
    /// <summary>
    /// Determines if a track is visible within the current viewport.
    /// Used for viewport-based virtualization of track rendering.
    /// </summary>
    public static bool IsTrackVisible(ScrollViewer scrollViewer, int trackIndex, double trackHeight)
    {
        if (scrollViewer == null) return true;
        
        var viewport = scrollViewer.ViewportHeight;
        var scrollOffset = scrollViewer.VerticalOffset;
        
        var trackTop = trackIndex * trackHeight;
        var trackBottom = trackTop + trackHeight;
        
        // Add buffer above and below visible area for smooth scrolling
        var bufferSize = trackHeight * 2;
        var visibleTop = scrollOffset - bufferSize;
        var visibleBottom = scrollOffset + viewport + bufferSize;
        
        return trackBottom >= visibleTop && trackTop <= visibleBottom;
    }
    
    /// <summary>
    /// Determines if a clip is visible within the current horizontal viewport.
    /// Used for viewport-based virtualization of clip rendering.
    /// </summary>
    public static bool IsClipVisible(ScrollViewer scrollViewer, double clipLeft, double clipWidth)
    {
        if (scrollViewer == null) return true;
        
        var viewport = scrollViewer.ViewportWidth;
        var scrollOffset = scrollViewer.HorizontalOffset;
        
        var clipRight = clipLeft + clipWidth;
        
        // Add buffer for smooth scrolling
        var bufferSize = Math.Max(clipWidth, 200); // Buffer at least 200px or clip width
        var visibleLeft = scrollOffset - bufferSize;
        var visibleRight = scrollOffset + viewport + bufferSize;
        
        return clipRight >= visibleLeft && clipLeft <= visibleRight;
    }
    
    /// <summary>
    /// Calculates optimal virtualization buffer size based on zoom level.
    /// </summary>
    public static double GetOptimalBufferSize(double zoomLevel)
    {
        // Larger buffer at higher zoom levels to maintain smooth scrolling
        return Math.Max(200, 1000 / zoomLevel);
    }
    
    /// <summary>
    /// Estimates memory usage for the current arrangement view.
    /// </summary>
    public static long EstimateMemoryUsage(int trackCount, int averageClipsPerTrack)
    {
        // Rough estimate: each track ~1KB, each clip ~0.5KB
        const int trackOverhead = 1024;
        const int clipOverhead = 512;
        
        return (trackCount * trackOverhead) + (trackCount * averageClipsPerTrack * clipOverhead);
    }
}