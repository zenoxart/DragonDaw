using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DAW.ViewModels;
using DAW.Models;
using DAW.Services;

namespace DAW.Views.Arrangement;

/// <summary>
/// Code-behind for the pixel-perfect FL Studio-style Arrangement view.
///
/// Improvements:
/// ────────────────
/// • Precise scroll synchronization with ScrollSynchronizer
/// • DPI-aware zoom handling
/// • Pixel-snapped coordinate calculations
/// • Optimized rendering performance
/// </summary>
public partial class ArrangementView : UserControl
{
    private ScrollSynchronizer? _scrollSync;
    private Action? _layoutSyncCleanup;
    private bool _isPlayheadDragging = false;
    private DispatcherTimer? _playheadTimer;
    private DateTime _playbackStartTime;
    private double _playbackStartBeat;

    public ArrangementView()
    {
        InitializeComponent();
        
        // Enable pixel-perfect rendering
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private ArrangementViewModel? Vm => DataContext as ArrangementViewModel;

    // ── Initialization ─────────────────────────────────────────────────────────
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollSync = new ScrollSynchronizer(
            TimelineScrollView,  // Main timeline ScrollViewer
            RulerScrollView,
            HeaderScrollView);
            
        // Setup robust layout synchronization
        _layoutSyncCleanup = LayoutSynchronizer.SetupHeightSynchronization(
            TimelineScrollView, 
            HeaderScrollView);
            
        // Initial height sync
        LayoutSynchronizer.SynchronizeHeights(TimelineScrollView, HeaderScrollView);
        
        // Initialize playhead timer
        InitializePlayheadTimer();
        
        // Subscribe to transport events
        SubscribeToTransportEvents();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _scrollSync?.Dispose();
        _scrollSync = null;
        
        _layoutSyncCleanup?.Invoke();
        _layoutSyncCleanup = null;
        
        // Stop and dispose playhead timer
        _playheadTimer?.Stop();
        _playheadTimer = null;
        
        // Unsubscribe from transport events
        UnsubscribeFromTransportEvents();
    }

    // ── Legacy scroll synchronization (kept for fallback) ─────────────────────

    private void TimelineScrollView_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Synchronize horizontal scrolling with ruler
        if (Math.Abs(e.HorizontalChange) > 0.001)
            RulerScrollView.ScrollToHorizontalOffset(Math.Round(e.HorizontalOffset));
            
        // Synchronize vertical scrolling with header panel
        if (Math.Abs(e.VerticalChange) > 0.001)
            HeaderScrollView.ScrollToVerticalOffset(Math.Round(e.VerticalOffset));
    }

    // ── DPI-aware zoom via Ctrl+MouseWheel ────────────────────────────────────

    private void TimelineScrollView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm is null) return;
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;

        // Get DPI information for precise calculations
        var dpiScale = VisualTreeHelper.GetDpi(this);
        
        // Beat position under the mouse pointer before zoom (pixel-snapped)
        var mouseCanvasX = Math.Round(e.GetPosition(TimelineGrid).X * dpiScale.DpiScaleX) / dpiScale.DpiScaleX;
        var beatAtMouse = Vm.PixelToBeat(mouseCanvasX);

        // Apply zoom step
        Vm.ZoomLevel = e.Delta > 0
            ? Math.Min(8.0, Vm.ZoomLevel * 1.12)
            : Math.Max(0.1, Vm.ZoomLevel / 1.12);

        // Scroll so that the same beat stays under the mouse (pixel-snapped)
        var newPixelAtBeat = Vm.BeatToPixel(beatAtMouse);
        var mouseViewportX = Math.Round(e.GetPosition(TimelineScrollView).X * dpiScale.DpiScaleX) / dpiScale.DpiScaleX;
        var targetOffset = Math.Round(newPixelAtBeat - mouseViewportX);
        
        TimelineScrollView.ScrollToHorizontalOffset(targetOffset);

        e.Handled = true;
    }

    // ── Size synchronization for layout changes ───────────────────────────────

    private void TimelineScrollView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Enhanced size synchronization - also handled by LayoutSynchronizer but adding double protection
        if (e.HeightChanged)
        {
            LayoutSynchronizer.SynchronizeHeights(TimelineScrollView, HeaderScrollView);
        }
    }

    // ── Track lane click → add clip ───────────────────────────────────────────

    private void TrackLane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (Vm is null) return;
        if (sender is not Border { DataContext: ArrangementTrackViewModel trackVm } laneBorder) return;

        // Ignore if the click landed on an existing clip (ClipControl handles those)
        if (!IsClickOnEmptyLane(e.OriginalSource as DependencyObject)) return;

        var pos = e.GetPosition(laneBorder);
        var beat = Vm.SnapToBeat(Math.Max(0.0, Vm.PixelToBeat(pos.X)));
        trackVm.AddClipAtBeat(beat);
        e.Handled = true;
    }

    /// <summary>
    /// Returns <c>true</c> if the event source is the lane background rather than a clip.
    /// Walks up the visual tree looking for a <see cref="ClipControl"/> ancestor.
    /// </summary>
    private static bool IsClickOnEmptyLane(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ClipControl) return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }

    // ── Drag and Drop ──────────────────────────────────────────────────────────

    private void TimelineScrollView_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        System.Diagnostics.Debug.WriteLine($"DragEnter - HasFileDrop: {e.Data.GetDataPresent(DataFormats.FileDrop)}, HasAudioFile: {e.Data.GetDataPresent(typeof(AudioBrowserFileViewModel))}");

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                var firstFile = files[0];
                var extension = System.IO.Path.GetExtension(firstFile);
                
                System.Diagnostics.Debug.WriteLine($"File dropped: {firstFile}, Extension: {extension}");
                
                if (AudioAnalysisService.IsSupportedFormat(extension))
                {
                    e.Effects = DragDropEffects.Copy;
                    System.Diagnostics.Debug.WriteLine("Supported audio format detected");
                }
            }
        }
        else if (e.Data.GetDataPresent(typeof(AudioBrowserFileViewModel)))
        {
            e.Effects = DragDropEffects.Copy;
            System.Diagnostics.Debug.WriteLine("AudioBrowserFileViewModel detected");
        }

        e.Handled = true;
    }

    private void TimelineScrollView_DragOver(object sender, DragEventArgs e)
    {
        // Same logic as DragEnter
        TimelineScrollView_DragEnter(sender, e);
    }

    private async void TimelineScrollView_Drop(object sender, DragEventArgs e)
    {
        // Mark handled immediately to prevent MainWindow's Window_Drop from also creating a track
        e.Handled = true;
        
        System.Diagnostics.Debug.WriteLine("=== AUDIO DROP OPERATION START ===");
        
        if (Vm == null) 
        {
            System.Diagnostics.Debug.WriteLine("ERROR: ViewModel is null");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"Track count: {Vm.Tracks.Count}");

        try
        {
            var position = e.GetPosition(TimelineGrid);
            var beat = Vm.PixelToBeat(position.X);
            var snappedBeat = Vm.SnapToBeat(beat);
            
            System.Diagnostics.Debug.WriteLine($"Drop position: X={position.X:F1}, Y={position.Y:F1}");
            System.Diagnostics.Debug.WriteLine($"Beat calculation: {beat:F2} -> snapped to {snappedBeat:F2}");
            
            // Determine which track was dropped on
            var trackIndex = (int)(position.Y / 52); // 52 is track height
            System.Diagnostics.Debug.WriteLine($"Calculated track index: {trackIndex} (Y={position.Y} / 52)");
            
            // Auto-create a new track if dropped outside existing tracks
            var trackWasAutoCreated = false;
            if (trackIndex < 0 || trackIndex >= Vm.Tracks.Count)
            {
                System.Diagnostics.Debug.WriteLine($"Track index out of range. Creating new track automatically.");
                Vm.AddEmptyTrackCommand.Execute(null);
                trackIndex = Vm.Tracks.Count - 1;
                trackWasAutoCreated = true;
                
                if (trackIndex < 0)
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: Failed to create new track.");
                    return;
                }
            }
            
            var targetTrack = Vm.Tracks[trackIndex];
            System.Diagnostics.Debug.WriteLine($"Target track: '{targetTrack.Name}'");
            
            string? audioFilePath = null;

            // Handle file drop from external source
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    audioFilePath = files[0];
                    System.Diagnostics.Debug.WriteLine($"External file drop: {audioFilePath}");
                }
            }
            // Handle drop from AudioBrowser
            else if (e.Data.GetDataPresent(typeof(AudioBrowserFileViewModel)))
            {
                var audioFile = (AudioBrowserFileViewModel)e.Data.GetData(typeof(AudioBrowserFileViewModel));
                audioFilePath = audioFile.FullPath;
                System.Diagnostics.Debug.WriteLine($"AudioBrowser file drop: {audioFilePath}");
            }

            if (!string.IsNullOrEmpty(audioFilePath))
            {
                // Rename auto-created track to match the audio file
                if (trackWasAutoCreated)
                {
                    targetTrack.Model.Title = System.IO.Path.GetFileNameWithoutExtension(audioFilePath);
                    targetTrack.Model.FilePath = audioFilePath;
                }
                else if (string.IsNullOrEmpty(targetTrack.Model.FilePath))
                {
                    targetTrack.Model.FilePath = audioFilePath;
                }

                var extension = System.IO.Path.GetExtension(audioFilePath);
                if (AudioAnalysisService.IsSupportedFormat(extension))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating audio clip for: {System.IO.Path.GetFileName(audioFilePath)}");
                    System.Diagnostics.Debug.WriteLine($"Current BPM: {Vm.BPM}");
                    
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    await targetTrack.AddAudioClipAtBeatAsync(snappedBeat, audioFilePath, Vm.BPM);
                    stopwatch.Stop();
                    
                    System.Diagnostics.Debug.WriteLine($"Audio clip created successfully in {stopwatch.ElapsedMilliseconds}ms");
                    System.Diagnostics.Debug.WriteLine($"Total clips in track: {targetTrack.Clips.Count}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Unsupported format: {extension}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ERROR: No audio file path found in drop data");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EXCEPTION in audio drop: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            System.Diagnostics.Debug.WriteLine("=== AUDIO DROP OPERATION END ===");
        }
    }

    // ── Playhead animation and timing ─────────────────────────────────────────

    /// <summary>
    /// Initializes the playhead timer for automatic playback
    /// </summary>
    private void InitializePlayheadTimer()
    {
        _playheadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS for smooth playhead movement
        };
        _playheadTimer.Tick += PlayheadTimer_Tick;
    }

    /// <summary>
    /// Subscribes to transport events from the main view model
    /// </summary>
    private void SubscribeToTransportEvents()
    {
        // For now, we'll implement a simple keyboard shortcut system
        // TODO: Connect to actual transport service when available
        this.KeyDown += ArrangementView_KeyDown;
    }

    /// <summary>
    /// Unsubscribes from transport events
    /// </summary>
    private void UnsubscribeFromTransportEvents()
    {
        this.KeyDown -= ArrangementView_KeyDown;
    }

    /// <summary>
    /// Handles keyboard shortcuts for transport control
    /// </summary>
    private void ArrangementView_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                IsPlaying = !IsPlaying;
                e.Handled = true;
                break;
            case Key.Home:
                SeekToBeat(0);
                e.Handled = true;
                break;
            case Key.End:
                if (Vm != null)
                    SeekToBeat(256 * 4); // TotalBars * BeatsPerBar
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Timer tick handler to update playhead position during playback
    /// </summary>
    private void PlayheadTimer_Tick(object? sender, EventArgs e)
    {
        if (Vm == null || _isPlayheadDragging) return;

        // TODO: Replace with actual audio engine playback state
        // For now, we'll simulate playback based on transport commands
        if (IsPlaying)
        {
            var elapsed = DateTime.Now - _playbackStartTime;
            var beatsPerSecond = Vm.BPM / 60.0;
            var currentBeat = _playbackStartBeat + (elapsed.TotalSeconds * beatsPerSecond);
            
            // Update playhead position
            Vm.PlayheadBeat = currentBeat;
            
            // Auto-scroll to keep playhead visible
            AutoScrollToPlayhead();
        }
    }

    /// <summary>
    /// Automatically scrolls the timeline to keep the playhead visible
    /// </summary>
    private void AutoScrollToPlayhead()
    {
        if (Vm == null) return;

        var playheadPixel = Vm.PlayheadPixel;
        var viewportWidth = TimelineScrollView.ViewportWidth;
        var currentOffset = TimelineScrollView.HorizontalOffset;
        
        // Check if playhead is outside visible area
        if (playheadPixel < currentOffset + 50) // Left margin
        {
            TimelineScrollView.ScrollToHorizontalOffset(Math.Max(0, playheadPixel - 100));
        }
        else if (playheadPixel > currentOffset + viewportWidth - 50) // Right margin
        {
            TimelineScrollView.ScrollToHorizontalOffset(playheadPixel - viewportWidth + 100);
        }
    }

    // ── Transport control integration ─────────────────────────────────────────

    private bool _isPlaying = false;
    
    /// <summary>
    /// Gets or sets whether playback is currently active
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                if (_isPlaying)
                {
                    StartPlayback();
                }
                else
                {
                    StopPlayback();
                }
            }
        }
    }

    /// <summary>
    /// Starts playback and playhead animation
    /// </summary>
    private void StartPlayback()
    {
        if (Vm == null) return;

        _playbackStartTime = DateTime.Now;
        _playbackStartBeat = Vm.PlayheadBeat;
        _playheadTimer?.Start();
        
        // TODO: Start audio engine playback
        // audioEngine.Play();
    }

    /// <summary>
    /// Stops playback and playhead animation
    /// </summary>
    private void StopPlayback()
    {
        _playheadTimer?.Stop();
        
        // TODO: Stop audio engine playback
        // audioEngine.Stop();
    }

    /// <summary>
    /// Seeks to a specific beat position and updates playback state
    /// </summary>
    public void SeekToBeat(double beat)
    {
        if (Vm == null) return;

        Vm.PlayheadBeat = beat;
        
        if (_isPlaying)
        {
            // Update playback start time for continuous playback
            _playbackStartTime = DateTime.Now;
            _playbackStartBeat = beat;
        }
        
        // TODO: Seek audio engine
        // audioEngine.Seek(TimeSpan.FromSeconds(beat * 60.0 / Vm.BPM));
    }

    // ── Playhead interaction (seeking) ────────────────────────────────────────

    /// <summary>
    /// Handles mouse down on the playhead for dragging/seeking
    /// </summary>
    private void Playhead_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Vm != null)
        {
            _isPlayheadDragging = true;
            
            // Capture mouse to handle dragging outside the control
            var playheadArea = sender as FrameworkElement;
            playheadArea?.CaptureMouse();
            
            // Visual feedback - make playhead slightly larger/brighter during drag
            UpdatePlayheadDragVisuals(true);
            
            // Update playhead position immediately
            UpdatePlayheadPosition(e.GetPosition(TimelineGrid));
            
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles mouse move during playhead dragging
    /// </summary>
    private void Playhead_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPlayheadDragging && e.LeftButton == MouseButtonState.Pressed && Vm != null)
        {
            UpdatePlayheadPosition(e.GetPosition(TimelineGrid));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles mouse up to end playhead dragging
    /// </summary>
    private void Playhead_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPlayheadDragging)
        {
            _isPlayheadDragging = false;
            
            // Release mouse capture
            var playheadArea = sender as FrameworkElement;
            playheadArea?.ReleaseMouseCapture();
            
            // Reset visual feedback
            UpdatePlayheadDragVisuals(false);
            
            e.Handled = true;
        }
    }

    /// <summary>
    /// Updates the playhead position based on mouse coordinates
    /// </summary>
    private void UpdatePlayheadPosition(Point mousePosition)
    {
        if (Vm == null) return;

        // Convert mouse X position to beat position
        var clampedX = Math.Max(0, Math.Min(mousePosition.X, Vm.TotalTimelineWidth));
        var beatPosition = Vm.PixelToBeat(clampedX);
        
        // Snap to grid if snapping is enabled
        if (Vm.SnapResolution > 0)
        {
            beatPosition = Math.Round(beatPosition / Vm.SnapResolution) * Vm.SnapResolution;
        }
        
        // Update the playhead position
        Vm.PlayheadBeat = beatPosition;
        
        // TODO: If you have an audio engine, seek to this position
        // audioEngine.Seek(TimeSpan.FromSeconds(beatPosition * 60.0 / Vm.BPM));
        
        SeekToBeat(beatPosition);
    }

    /// <summary>
    /// Updates playhead visuals during drag operations
    /// </summary>
    private void UpdatePlayheadDragVisuals(bool isDragging)
    {
        if (FindName("PlayheadTriangle") is Path triangle && FindName("PlayheadLine") is Rectangle line)
        {
            if (isDragging)
            {
                // Make playhead more visible during dragging
                triangle.Fill = new SolidColorBrush(Color.FromRgb(255, 99, 99)); // Brighter red
                line.Fill = new SolidColorBrush(Color.FromRgb(255, 99, 99));
                line.Width = 2.0; // Slightly thicker
            }
            else
            {
                // Reset to normal appearance
                triangle.Fill = new SolidColorBrush(Color.FromRgb(230, 57, 70)); // Dragon red
                line.Fill = new SolidColorBrush(Color.FromRgb(230, 57, 70));
                line.Width = 1.5;
            }
        }
    }

    // ── Timeline click-to-seek functionality ──────────────────────────────────

    /// <summary>
    /// Handles clicking on the timeline to seek playhead
    /// </summary>
    private void TimelineGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Vm != null)
        {
            var clickPosition = e.GetPosition(TimelineGrid);
            UpdatePlayheadPosition(clickPosition);
            e.Handled = true;
        }
    }
}
