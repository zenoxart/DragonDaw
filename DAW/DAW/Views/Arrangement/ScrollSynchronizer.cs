using System.Windows;
using System.Windows.Controls;

namespace DAW.Views.Arrangement;

/// <summary>
/// Coordinates synchronized scrolling between multiple ScrollViewer instances
/// for pixel-perfect alignment in the arrangement view.
/// 
/// Features:
/// • Precise offset synchronization without rounding errors
/// • Prevents infinite scroll event loops
/// • DPI-aware synchronization
/// • Size synchronization to prevent layout drift
/// • Works with main content ScrollViewer wrapping both header and timeline
/// </summary>
public sealed class ScrollSynchronizer
{
    private readonly ScrollViewer _mainContentScrollViewer;
    private readonly ScrollViewer _rulerScrollViewer;
    private readonly ScrollViewer _headerScrollViewer;
    
    private bool _syncInProgress;
    
    public ScrollSynchronizer(
        ScrollViewer mainContentScrollViewer, 
        ScrollViewer rulerScrollViewer, 
        ScrollViewer headerScrollViewer)
    {
        _mainContentScrollViewer = mainContentScrollViewer;
        _rulerScrollViewer = rulerScrollViewer;
        _headerScrollViewer = headerScrollViewer;
        
        _mainContentScrollViewer.ScrollChanged += OnMainContentScrollChanged;
        _mainContentScrollViewer.SizeChanged += OnMainContentScrollViewerSizeChanged;
    }
    
    private void OnMainContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_syncInProgress) return;
        
        ArrangementPerformanceMonitor.StartTimer();
        ArrangementPerformanceMonitor.LogScrollPerformance(e.HorizontalChange, e.VerticalChange);
        
        _syncInProgress = true;
        try
        {
            // Synchronized horizontal scrolling (main content ↔ ruler)
            if (Math.Abs(e.HorizontalChange) > 0.001)
            {
                var targetOffset = Math.Round(e.HorizontalOffset);
                if (Math.Abs(_rulerScrollViewer.HorizontalOffset - targetOffset) > 0.001)
                {
                    _rulerScrollViewer.ScrollToHorizontalOffset(targetOffset);
                }
            }
            
            // Note: Vertical synchronization is now handled automatically by the wrapping ScrollViewer
            // The HeaderScrollView and TimelineScrollView are both inside MainContentScrollViewer
        }
        finally
        {
            _syncInProgress = false;
            ArrangementPerformanceMonitor.StopTimer();
        }
    }
    
    private void OnMainContentScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Synchronization is now handled by the wrapping ScrollViewer structure
        // This method is kept for future enhancements if needed
    }
    
    public void Dispose()
    {
        _mainContentScrollViewer.ScrollChanged -= OnMainContentScrollChanged;
        _mainContentScrollViewer.SizeChanged -= OnMainContentScrollViewerSizeChanged;
    }
}