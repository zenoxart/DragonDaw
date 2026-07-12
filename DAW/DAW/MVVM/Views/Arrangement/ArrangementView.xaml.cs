using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DAW.MVVM.Models.Sequencer;
using DAW.MVVM.ViewModels;
using DAW.MVVM.ViewModels.Sequencer;
using DAW.MVVM.Views.Arrangement;
using DAW.Services;

namespace DAW.MVVM.Views.Arrangement;

public partial class ArrangementView : UserControl
{
    private ScrollSynchronizer? _scrollSync;
    private Action?             _layoutSyncCleanup;
    private bool                _isPlayheadDragging;
    private DispatcherTimer?    _playheadTimer;
    private DateTime            _playbackStartTime;
    private double              _playbackStartBeat;

    // Zoom selection
    private bool   _isZoomSelecting;
    private Point  _zoomSelectOrigin;
    private double _zoomSelectOriginBeat;
    private double _previousZoomLevel;
    private double _zoomSelectTrackHeightBefore;

    // Pattern drag
    private Point _patternDragStart;
    private bool  _patternDragReady;

    // ── Pattern browser flat list ──────────────────────────────────────────
    private readonly ObservableCollection<PatternBrowserItem> _browserItems = [];

    private Border? ZoomRect => FindName("ZoomSelectionRect") as Border;

    public ArrangementView()
    {
        InitializeComponent();
        UseLayoutRounding   = true;
        SnapsToDevicePixels = true;

        // Wire the flat browser list to the ItemsControl immediately
        PatternBrowserList.ItemsSource = _browserItems;

        Loaded           += OnLoaded;
        Unloaded         += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private ArrangementViewModel? Vm => DataContext as ArrangementViewModel;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollSync        = new ScrollSynchronizer(TimelineScrollView, RulerScrollView, HeaderScrollView);
        _layoutSyncCleanup = LayoutSynchronizer.SetupHeightSynchronization(TimelineScrollView, HeaderScrollView);
        LayoutSynchronizer.SynchronizeHeights(TimelineScrollView, HeaderScrollView);
        InitializePlayheadTimer();
        KeyDown += ArrangementView_KeyDown;

        SubscribePatternVm(Vm?.PatternVm);
        RebuildBrowserList();

        // Sync the column width with the current visibility state
        SyncPatternBrowserColumn();
        if (Vm != null) Vm.PropertyChanged += ArrangementVm_PropertyChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _scrollSync?.Dispose();
        _scrollSync = null;
        _layoutSyncCleanup?.Invoke();
        _layoutSyncCleanup = null;
        _playheadTimer?.Stop();
        _playheadTimer = null;
        KeyDown -= ArrangementView_KeyDown;
        UnsubscribePatternVm(Vm?.PatternVm);
        if (Vm != null) Vm.PropertyChanged -= ArrangementVm_PropertyChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ArrangementViewModel oldVm) UnsubscribePatternVm(oldVm.PatternVm);
        if (e.NewValue is ArrangementViewModel newVm)
        {
            SubscribePatternVm(newVm.PatternVm);
            newVm.PropertyChanged += ArrangementVm_PropertyChanged;
        }
        RebuildBrowserList();
        SyncPatternBrowserColumn();
    }

    private void ArrangementVm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArrangementViewModel.IsPatternBrowserVisible))
            SyncPatternBrowserColumn();
    }

    /// <summary>
    /// Collapses or restores the pattern browser column (col 0) and splitter (col 1)
    /// so no dead space remains when the panel is hidden.
    /// </summary>
    private void SyncPatternBrowserColumn()
    {
        var grid = Content as System.Windows.Controls.Grid;
        if (grid == null) return;
        bool visible = Vm?.IsPatternBrowserVisible ?? true;
        // Col 0 = pattern browser, Col 1 = splitter
        grid.ColumnDefinitions[0].Width = visible
            ? new System.Windows.GridLength(160, System.Windows.GridUnitType.Pixel)
            : new System.Windows.GridLength(0);
        grid.ColumnDefinitions[1].Width = visible
            ? new System.Windows.GridLength(4)
            : new System.Windows.GridLength(0);
    }

    // ── Pattern VM subscriptions ───────────────────────────────────────────

    private void SubscribePatternVm(PatternViewModel? pvm)
    {
        if (pvm == null) return;
        pvm.PropertyChanged        += PatternVm_PropertyChanged;
        pvm.AllPatterns.CollectionChanged += AllPatterns_CollectionChanged;
        pvm.Channels.CollectionChanged    += Channels_CollectionChanged;
    }

    private void UnsubscribePatternVm(PatternViewModel? pvm)
    {
        if (pvm == null) return;
        pvm.PropertyChanged        -= PatternVm_PropertyChanged;
        pvm.AllPatterns.CollectionChanged -= AllPatterns_CollectionChanged;
        pvm.Channels.CollectionChanged    -= Channels_CollectionChanged;
    }

    private void PatternVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PatternViewModel.ActivePattern)
                           or nameof(PatternViewModel.Channels))
            RebuildBrowserList();
    }

    private void AllPatterns_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildBrowserList();

    private void Channels_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildBrowserList();

    // ── Flat browser list builder ──────────────────────────────────────────
    /// <summary>
    /// Rebuilds _browserItems from scratch:
    ///   for each pattern → one header item
    ///     for each channel in that pattern → one indented channel item
    ///
    /// This gives the FL-Studio-style tree where channels live directly
    /// below their pattern and scroll as one contiguous list.
    /// </summary>
    private void RebuildBrowserList()
    {
        var pvm = Vm?.PatternVm;
        _browserItems.Clear();
        if (pvm == null) return;

        var active = pvm.ActivePattern;

        foreach (var pattern in pvm.AllPatterns)
        {
            // Pattern header row
            _browserItems.Add(new PatternBrowserItem
            {
                IsPatternRow = true,
                IsActive     = pattern == active,
                Icon         = "▦",
                Label        = pattern.Name,
                Pattern      = pattern,
            });

            // Channel rows — only for the active pattern (collapsed for others)
            // This mirrors FL Studio: you see channels of the selected pattern only.
            if (pattern == active)
            {
                foreach (var ch in pvm.Channels)
                {
                    _browserItems.Add(new PatternBrowserItem
                    {
                        IsPatternRow = false,
                        IsActive     = false,
                        Icon         = ch.PluginIcon,
                        Label        = ch.Name,
                        DotColor     = ch.ChannelColor,
                        Channel      = ch,
                    });
                }
            }
        }
    }

    // ── Browser item interaction ───────────────────────────────────────────

    private void BrowserItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _patternDragStart = e.GetPosition(this);
        _patternDragReady = true;

        if (sender is not FrameworkElement { DataContext: PatternBrowserItem item }) return;

        if (item.IsPatternRow && item.Pattern != null && Vm?.PatternVm is { } pvm)
        {
            pvm.ActivePattern = item.Pattern;
            // RebuildBrowserList is triggered via PatternVm_PropertyChanged
        }
    }

    private void BrowserItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_patternDragReady || e.LeftButton != MouseButtonState.Pressed) return;
        var diff = e.GetPosition(this) - _patternDragStart;
        if (Math.Abs(diff.X) < 6 && Math.Abs(diff.Y) < 6) return;
        _patternDragReady = false;

        if (sender is not FrameworkElement { DataContext: PatternBrowserItem item }) return;

        DataObject data;
        if (item.IsPatternRow && item.Pattern != null)
            data = new DataObject(typeof(PatternModel), item.Pattern);
        else if (!item.IsPatternRow && item.Channel != null)
            data = new DataObject(typeof(ChannelViewModel), item.Channel);
        else
            return;

        DragDrop.DoDragDrop(sender as DependencyObject ?? this, data, DragDropEffects.Copy);
    }

    // Keep old handler names for compatibility (were wired in previous XAML iterations)
    private void PatternItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => BrowserItem_MouseLeftButtonDown(sender, e);
    private void PatternItem_MouseMove(object sender, MouseEventArgs e)
        => BrowserItem_MouseMove(sender, e);
    private void ChannelItem_MouseMove(object sender, MouseEventArgs e)
        => BrowserItem_MouseMove(sender, e);

    // ── Scroll synchronization ─────────────────────────────────────────────

    private void TimelineScrollView_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.HorizontalChange) > 0.001)
            RulerScrollView.ScrollToHorizontalOffset(Math.Round(e.HorizontalOffset));
        if (Math.Abs(e.VerticalChange) > 0.001)
            HeaderScrollView.ScrollToVerticalOffset(Math.Round(e.VerticalOffset));
    }

    // ── Zoom via Ctrl+Scroll ───────────────────────────────────────────────

    private void TimelineScrollView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Vm is null) return;
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;
        var dpi        = VisualTreeHelper.GetDpi(this);
        var canvasX    = Math.Round(e.GetPosition(TimelineGrid).X * dpi.DpiScaleX) / dpi.DpiScaleX;
        var beat       = Vm.PixelToBeat(canvasX);
        Vm.ZoomLevel   = e.Delta > 0 ? Math.Min(8.0, Vm.ZoomLevel * 1.12) : Math.Max(0.1, Vm.ZoomLevel / 1.12);
        var viewportX  = Math.Round(e.GetPosition(TimelineScrollView).X * dpi.DpiScaleX) / dpi.DpiScaleX;
        TimelineScrollView.ScrollToHorizontalOffset(Math.Round(Vm.BeatToPixel(beat) - viewportX));
        e.Handled = true;
    }

    private void TimelineScrollView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged)
            LayoutSynchronizer.SynchronizeHeights(TimelineScrollView, HeaderScrollView);
    }

    // ── Track lane click → add clip ────────────────────────────────────────

    private void TrackLane_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Vm is null) return;
        if (sender is not Border { DataContext: ArrangementTrackViewModel trackVm } lane) return;
        Vm.SelectTrack(trackVm, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        if (!IsClickOnEmptyLane(e.OriginalSource as DependencyObject)) return;
        var beat = Vm.SnapToBeat(Math.Max(0.0, Vm.PixelToBeat(e.GetPosition(lane).X)));
        trackVm.AddClipAtBeat(beat);
        e.Handled = true;
    }

    private static bool IsClickOnEmptyLane(DependencyObject? source)
    {
        for (var cur = source; cur != null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is ClipControl) return false;
        return true;
    }

    // ── Drag & drop into timeline ──────────────────────────────────────────

    private void TimelineScrollView_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files?.Any(f => AudioAnalysisService.IsSupportedFormat(System.IO.Path.GetExtension(f))) == true)
                e.Effects = DragDropEffects.Copy;
        }
        else if (e.Data.GetDataPresent(typeof(AudioBrowserFileViewModel)) ||
                 e.Data.GetDataPresent(typeof(ChannelViewModel))          ||
                 e.Data.GetDataPresent(typeof(PatternModel)))
        {
            e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void TimelineScrollView_DragOver(object sender, DragEventArgs e)
        => TimelineScrollView_DragEnter(sender, e);

    private async void TimelineScrollView_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Vm == null) return;
        var pos  = e.GetPosition(TimelineGrid);
        var beat = Vm.SnapToBeat(Math.Max(0.0, Vm.PixelToBeat(pos.X)));

        if (e.Data.GetDataPresent(typeof(PatternModel)))
        {
            var pattern = (PatternModel)e.Data.GetData(typeof(PatternModel));
            if (pattern != null)
            {
                Vm.AddEmptyTrackCommand.Execute(null);
                var t = Vm.Tracks.LastOrDefault();
                if (t != null) { t.Model.Title = pattern.Name; t.AddClipAtBeat(beat); }
            }
            return;
        }

        if (e.Data.GetDataPresent(typeof(ChannelViewModel)))
        {
            var ch = (ChannelViewModel)e.Data.GetData(typeof(ChannelViewModel));
            if (ch != null)
            {
                Vm.AddEmptyTrackCommand.Execute(null);
                var t = Vm.Tracks.LastOrDefault();
                if (t != null)
                {
                    t.Model.Title    = ch.Name;
                    t.Model.FilePath = ch.Model.SamplePath ?? string.Empty;
                    if (!string.IsNullOrEmpty(ch.Model.SamplePath) && System.IO.File.Exists(ch.Model.SamplePath))
                        await t.AddAudioClipAtBeatAsync(beat, ch.Model.SamplePath, Vm.BPM);
                    else
                        t.AddClipAtBeat(beat);
                }
            }
            return;
        }

        var trackIdx = (int)(pos.Y / Vm.CurrentTrackHeight);
        var paths    = new List<string>();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                if (AudioAnalysisService.IsSupportedFormat(System.IO.Path.GetExtension(f))) paths.Add(f);
        else if (e.Data.GetDataPresent(typeof(AudioBrowserFileViewModel)))
            paths.Add(((AudioBrowserFileViewModel)e.Data.GetData(typeof(AudioBrowserFileViewModel))).FullPath);

        for (int i = 0; i < paths.Count; i++)
        {
            var idx = trackIdx + i;
            bool created = idx < 0 || idx >= Vm.Tracks.Count;
            if (created) { Vm.AddEmptyTrackCommand.Execute(null); idx = Vm.Tracks.Count - 1; }
            var t = Vm.Tracks[idx];
            if (created) { t.Model.Title = System.IO.Path.GetFileNameWithoutExtension(paths[i]); t.Model.FilePath = paths[i]; }
            else if (string.IsNullOrEmpty(t.Model.FilePath)) t.Model.FilePath = paths[i];
            await t.AddAudioClipAtBeatAsync(beat, paths[i], Vm.BPM);
        }
    }

    // ── Playhead ───────────────────────────────────────────────────────────

    private void InitializePlayheadTimer()
    {
        _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _playheadTimer.Tick += (_, _) =>
        {
            if (Vm == null || _isPlayheadDragging || !IsPlaying) return;
            Vm.PlayheadBeat = _playbackStartBeat + (DateTime.Now - _playbackStartTime).TotalSeconds * (Vm.BPM / 60.0);
            var px = Vm.PlayheadPixel; var vw = TimelineScrollView.ViewportWidth; var off = TimelineScrollView.HorizontalOffset;
            if      (px < off + 50)      TimelineScrollView.ScrollToHorizontalOffset(Math.Max(0, px - 100));
            else if (px > off + vw - 50) TimelineScrollView.ScrollToHorizontalOffset(px - vw + 100);
        };
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            _isPlaying = value;
            if (_isPlaying) { _playbackStartTime = DateTime.Now; _playbackStartBeat = Vm?.PlayheadBeat ?? 0; _playheadTimer?.Start(); }
            else            { _playheadTimer?.Stop(); }
        }
    }

    public void SeekToBeat(double beat)
    {
        if (Vm == null) return;
        Vm.PlayheadBeat = beat;
        if (_isPlaying) { _playbackStartTime = DateTime.Now; _playbackStartBeat = beat; }
    }

    private void ArrangementView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { IsPlaying = !IsPlaying; e.Handled = true; }
        else if (e.Key == Key.Home) { SeekToBeat(0); e.Handled = true; }
    }

    private void Ruler_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        UpdatePlayheadPos(new Point(Math.Max(0, e.GetPosition(RulerControl).X), 0));
        e.Handled = true;
    }

    private void Playhead_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || Vm == null) return;
        _isPlayheadDragging = true;
        (sender as FrameworkElement)?.CaptureMouse();
        SetPlayheadDragVisuals(true);
        UpdatePlayheadPos(e.GetPosition(TimelineGrid));
        e.Handled = true;
    }

    private void Playhead_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPlayheadDragging || e.LeftButton != MouseButtonState.Pressed) return;
        UpdatePlayheadPos(e.GetPosition(TimelineGrid));
        e.Handled = true;
    }

    private void Playhead_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPlayheadDragging) return;
        _isPlayheadDragging = false;
        (sender as FrameworkElement)?.ReleaseMouseCapture();
        SetPlayheadDragVisuals(false);
        e.Handled = true;
    }

    private void UpdatePlayheadPos(Point p)
    {
        if (Vm == null) return;
        var beat = Vm.PixelToBeat(Math.Max(0, Math.Min(p.X, Vm.TotalTimelineWidth)));
        if (Vm.SnapResolution > 0) beat = Math.Round(beat / Vm.SnapResolution) * Vm.SnapResolution;
        SeekToBeat(beat);
    }

    private void SetPlayheadDragVisuals(bool drag)
    {
        var col = drag ? Color.FromRgb(255, 99, 99) : Color.FromRgb(230, 57, 70);
        var b   = new SolidColorBrush(col);
        if (FindName("PlayheadTriangle") is Path tri)   tri.Fill  = b;
        if (FindName("PlayheadLine")     is System.Windows.Shapes.Rectangle ln) { ln.Fill = b; ln.Width = drag ? 2.0 : 1.5; }
    }

    private void TimelineGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Vm != null)
        { UpdatePlayheadPos(e.GetPosition(TimelineGrid)); e.Handled = true; }
    }

    // ── Ctrl+RightClick zoom ───────────────────────────────────────────────

    private void TimelineScrollView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null || (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))) return;
        _isZoomSelecting             = true;
        _zoomSelectOrigin            = e.GetPosition(TimelineGrid);
        _zoomSelectOriginBeat        = Vm.PixelToBeat(_zoomSelectOrigin.X);
        _previousZoomLevel           = Vm.ZoomLevel;
        _zoomSelectTrackHeightBefore = Vm.CurrentTrackHeight;
        if (ZoomRect is { } r) { Canvas.SetLeft(r, _zoomSelectOrigin.X); Canvas.SetTop(r, _zoomSelectOrigin.Y); r.Width = 0; r.Height = 0; r.Visibility = Visibility.Visible; }
        TimelineScrollView.CaptureMouse();
        e.Handled = true;
    }

    private void TimelineScrollView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isZoomSelecting) return;
        var c = e.GetPosition(TimelineGrid);
        if (ZoomRect is { } r) { Canvas.SetLeft(r, Math.Min(_zoomSelectOrigin.X, c.X)); Canvas.SetTop(r, Math.Min(_zoomSelectOrigin.Y, c.Y)); r.Width = Math.Abs(c.X - _zoomSelectOrigin.X); r.Height = Math.Abs(c.Y - _zoomSelectOrigin.Y); }
    }

    private void TimelineScrollView_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isZoomSelecting) return;
        _isZoomSelecting = false;
        if (ZoomRect is { } r) r.Visibility = Visibility.Collapsed;
        TimelineScrollView.ReleaseMouseCapture();
        if (Vm is null) { e.Handled = true; return; }
        var rel = e.GetPosition(TimelineGrid);
        if (Math.Abs(rel.X - _zoomSelectOrigin.X) < 8)
            ZoomOut(_zoomSelectOrigin);
        else
            ZoomToRect(Math.Min(_zoomSelectOrigin.X, rel.X), Math.Max(_zoomSelectOrigin.X, rel.X),
                       Math.Min(_zoomSelectOrigin.Y, rel.Y), Math.Max(_zoomSelectOrigin.Y, rel.Y));
        e.Handled = true;
    }

    private void ZoomToRect(double l, double r, double t, double b)
    {
        if (Vm is null) return;
        var span = Vm.PixelToBeat(r) - Vm.PixelToBeat(l);
        if (span <= 0) return;
        Vm.ZoomLevel = Math.Clamp((TimelineScrollView.ViewportWidth / span) / (Vm.PixelsPerBeat / Vm.ZoomLevel), 0.1, 8.0);
        TimelineScrollView.ScrollToHorizontalOffset(Math.Round(Vm.BeatToPixel(Vm.PixelToBeat(l))));
        var h = b - t;
        if (h > 0 && h / _zoomSelectTrackHeightBefore >= 0.5) Vm.CurrentTrackHeight = TimelineScrollView.ViewportHeight / (h / _zoomSelectTrackHeightBefore);
        TimelineScrollView.ScrollToVerticalOffset(Math.Round(t / _zoomSelectTrackHeightBefore * Vm.CurrentTrackHeight));
    }

    private void ZoomOut(Point p)
    {
        if (Vm is null) return;
        var beat = Vm.PixelToBeat(p.X); var track = p.Y / Vm.CurrentTrackHeight;
        Vm.ZoomLevel          = Math.Max(0.1, Vm.ZoomLevel / 1.5);
        Vm.CurrentTrackHeight = Math.Min(Vm.CurrentTrackHeight / 1.5, TimelineScrollView.ViewportHeight / Math.Max(1, Vm.Tracks.Count));
        TimelineScrollView.ScrollToHorizontalOffset(Math.Round(Vm.BeatToPixel(beat) - (p.X - TimelineScrollView.HorizontalOffset)));
        TimelineScrollView.ScrollToVerticalOffset(Math.Round(track * Vm.CurrentTrackHeight - (p.Y - TimelineScrollView.VerticalOffset)));
    }

    private void Solo_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        var s = Vm.Tracks.Where(t => t.Model.IsSolo).ToList();
        if (s.Count > 1) { foreach (var t in s) t.Model.IsSolo = false; e.Handled = true; }
    }
}
