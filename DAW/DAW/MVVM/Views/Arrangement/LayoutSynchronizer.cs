using System.Windows;
using System.Windows.Controls;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// Ensures that HeaderScrollView and TimelineScrollView maintain synchronized heights
/// even when the application layout changes due to browser interactions or window resizing.
/// </summary>
public static class LayoutSynchronizer
{
    /// <summary>
    /// Synchronizes the height of two ScrollViewer controls to prevent layout drift.
    /// </summary>
    /// <param name="masterScrollViewer">The ScrollViewer that determines the size</param>
    /// <param name="slaveScrollViewer">The ScrollViewer that follows the master</param>
    public static void SynchronizeHeights(ScrollViewer masterScrollViewer, ScrollViewer slaveScrollViewer)
    {
        if (masterScrollViewer == null || slaveScrollViewer == null) return;
        
        // Use a tolerance to prevent micro-adjustments
        const double tolerance = 1.0;
        
        if (Math.Abs(masterScrollViewer.ActualHeight - slaveScrollViewer.ActualHeight) > tolerance)
        {
            slaveScrollViewer.Height = masterScrollViewer.ActualHeight;
        }
    }
    
    /// <summary>
    /// Sets up automatic height synchronization between two ScrollViewer controls.
    /// Returns an action to remove the synchronization.
    /// </summary>
    public static Action SetupHeightSynchronization(ScrollViewer masterScrollViewer, ScrollViewer slaveScrollViewer)
    {
        SizeChangedEventHandler handler = (_, e) =>
        {
            if (e.HeightChanged)
            {
                SynchronizeHeights(masterScrollViewer, slaveScrollViewer);
            }
        };
        
        masterScrollViewer.SizeChanged += handler;
        
        // Return cleanup action
        return () => masterScrollViewer.SizeChanged -= handler;
    }
    
    /// <summary>
    /// Prevents ScrollViewer size changes when specific conditions are met.
    /// Useful for preventing unwanted layout changes during browser interactions.
    /// </summary>
    public static void FreezeScrollViewerSize(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null) return;
        
        var currentHeight = scrollViewer.ActualHeight;
        var currentWidth = scrollViewer.ActualWidth;
        
        if (currentHeight > 0) scrollViewer.Height = currentHeight;
        if (currentWidth > 0) scrollViewer.Width = currentWidth;
    }
    
    /// <summary>
    /// Unfreezes a ScrollViewer's size, allowing it to resize naturally.
    /// </summary>
    public static void UnfreezeScrollViewerSize(ScrollViewer scrollViewer)
    {
        if (scrollViewer == null) return;
        
        scrollViewer.ClearValue(FrameworkElement.HeightProperty);
        scrollViewer.ClearValue(FrameworkElement.WidthProperty);
    }
}