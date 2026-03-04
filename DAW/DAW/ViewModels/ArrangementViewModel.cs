using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.Models;

namespace DAW.ViewModels;

/// <summary>
/// Main ViewModel for the FL Studio-style Arrangement / Playlist view.
///
/// ── Beat-to-Pixel Mapping ──────────────────────────────────────────────────
///
///   BasePixelsPerBeat = 80 px   (at ZoomLevel 1.0)
///   PixelsPerBeat     = BasePixelsPerBeat × ZoomLevel
///
///   Canvas-absolute coordinates (ScrollViewer handles viewport offset):
///     PixelLeft  = StartBeat    × PixelsPerBeat   →  clip left edge
///     PixelWidth = LengthInBeats × PixelsPerBeat  →  clip width
///     BeatToPixel(beat) = beat × PixelsPerBeat
///     PixelToBeat(px)   = px   / PixelsPerBeat
///
///   Snap grid:
///     SnapToBeat(beat) = Round(beat / SnapResolution) × SnapResolution
///     1.0 = quarter-note  |  0.5 = eighth-note  |  0.25 = sixteenth-note
///
/// ── Rendering Strategy ────────────────────────────────────────────────────
///   Background grid  → TimelineGridControl (single DrawingContext pass)
///   Ruler            → RulerControl        (single DrawingContext pass)
///   Clips            → ItemsControl with Canvas panel; virtualized rows
///   Playhead         → Canvas-overlaid Rectangle bound to PlayheadPixel
/// </summary>
public sealed class ArrangementViewModel : INotifyPropertyChanged
{
    // ── Layout constants ───────────────────────────────────────────────────────
    public const double BasePixelsPerBeat = 80.0;
    public const double TrackHeight       = 52.0;
    public const int    BeatsPerBar       = 4;

    private const double MinZoom   = 0.1;
    private const double MaxZoom   = 8.0;
    private const int    TotalBars = 256;

    // ── Private state ──────────────────────────────────────────────────────────
    private readonly MainViewModel _mainVm;
    private double _zoomLevel      = 1.0;
    private double _playheadBeat;
    private double _snapResolution = 1.0;
    private ArrangementClipViewModel? _selectedClip;

    public ArrangementViewModel(MainViewModel mainVm)
    {
        _mainVm = mainVm;
        Tracks  = [];

        foreach (var track in _mainVm.Tracks)
            Tracks.Add(new ArrangementTrackViewModel(track, this));

        _mainVm.Tracks.CollectionChanged += OnMainTracksChanged;
        _mainVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.BPM))
                OnPropertyChanged(nameof(BPM));
        };

        ZoomInCommand  = new RelayCommand(() => ZoomLevel = Math.Min(MaxZoom, ZoomLevel * 1.25));
        ZoomOutCommand = new RelayCommand(() => ZoomLevel = Math.Max(MinZoom, ZoomLevel / 1.25));
        ZoomResetCommand = new RelayCommand(() => ZoomLevel = 1.0);

        SnapQuarterCommand   = new RelayCommand(() => SnapResolution = 1.0);
        SnapEighthCommand    = new RelayCommand(() => SnapResolution = 0.5);
        SnapSixteenthCommand = new RelayCommand(() => SnapResolution = 0.25);
        AddEmptyTrackCommand = new RelayCommand(() => AddEmptyTrack());
    }

    // ── Track collection ───────────────────────────────────────────────────────
    public ObservableCollection<ArrangementTrackViewModel> Tracks { get; }

    // ── Beat-to-Pixel mapping ──────────────────────────────────────────────────

    /// <summary>Pixels per beat at the current zoom level.</summary>
    public double PixelsPerBeat => BasePixelsPerBeat * ZoomLevel;

    /// <summary>Converts a beat position to a canvas-absolute pixel X coordinate with pixel snapping.</summary>
    public double BeatToPixel(double beat) => Math.Round(beat * PixelsPerBeat);

    /// <summary>Converts a canvas-absolute pixel X coordinate to a beat position.</summary>
    public double PixelToBeat(double pixel) => pixel / PixelsPerBeat;

    /// <summary>Converts a beat count to a pixel width with pixel snapping.</summary>
    public double BeatsToPixels(double beats) => Math.Round(beats * PixelsPerBeat);

    /// <summary>Snaps a beat value to the nearest snap-grid point.</summary>
    public double SnapToBeat(double beat) =>
        Math.Round(beat / SnapResolution) * SnapResolution;

    // ── Zoom ───────────────────────────────────────────────────────────────────
    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            if (!SetField(ref _zoomLevel, Math.Clamp(value, MinZoom, MaxZoom))) return;
            OnPropertyChanged(nameof(PixelsPerBeat));
            OnPropertyChanged(nameof(TotalTimelineWidth));
            OnPropertyChanged(nameof(PlayheadPixel));
            OnPropertyChanged(nameof(ZoomDisplay));
            NotifyAllClipPixelsChanged();
        }
    }

    public string ZoomDisplay => $"{ZoomLevel * 100:F0}%";

    // ── Snap ───────────────────────────────────────────────────────────────────
    public double SnapResolution
    {
        get => _snapResolution;
        set => SetField(ref _snapResolution, value);
    }

    // ── Playhead ───────────────────────────────────────────────────────────────
    public double PlayheadBeat
    {
        get => _playheadBeat;
        set
        {
            if (!SetField(ref _playheadBeat, Math.Max(0, value))) return;
            OnPropertyChanged(nameof(PlayheadPixel));
        }
    }

    /// <summary>Canvas-absolute pixel position of the playhead needle with pixel snapping.</summary>
    public double PlayheadPixel => Math.Round(BeatToPixel(PlayheadBeat));

    // ── Layout ─────────────────────────────────────────────────────────────────

    /// <summary>Total width of the timeline canvas in pixels.</summary>
    public double TotalTimelineWidth => TotalBars * BeatsPerBar * PixelsPerBeat;

    /// <summary>Total height of the timeline canvas in pixels.</summary>
    public double TotalTimelineHeight => Math.Max(Tracks.Count * TrackHeight, TrackHeight);

    public double BPM => _mainVm.BPM;

    // ── Selection ──────────────────────────────────────────────────────────────
    public ArrangementClipViewModel? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (_selectedClip != null) _selectedClip.Model.IsSelected = false;
            SetField(ref _selectedClip, value);
            if (_selectedClip != null) _selectedClip.Model.IsSelected = true;
        }
    }

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand ZoomInCommand      { get; }
    public ICommand ZoomOutCommand     { get; }
    public ICommand ZoomResetCommand   { get; }
    public ICommand SnapQuarterCommand   { get; }
    public ICommand SnapEighthCommand    { get; }
    public ICommand SnapSixteenthCommand { get; }
    public ICommand AddEmptyTrackCommand { get; }

    // ── Internal ───────────────────────────────────────────────────────────────

    private void OnMainTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (Track t in e.NewItems)
                Tracks.Add(new ArrangementTrackViewModel(t, this));

        if (e.OldItems != null)
            foreach (Track t in e.OldItems)
            {
                var vm = Tracks.FirstOrDefault(tv => tv.Model == t);
                if (vm != null) Tracks.Remove(vm);
            }

        OnPropertyChanged(nameof(TotalTimelineHeight));
    }

    /// <summary>
    /// Removes a track from the arrangement.
    /// </summary>
    public void RemoveTrack(ArrangementTrackViewModel trackVm)
    {
        var track = trackVm.Model;
        _mainVm.Tracks.Remove(track); // This will trigger OnMainTracksChanged
    }

    /// <summary>
    /// Adds a new empty track to the arrangement.
    /// </summary>
    private void AddEmptyTrack()
    {
        _mainVm.AddEmptyTrack();
    }

    private void NotifyAllClipPixelsChanged()
    {
        foreach (var track in Tracks)
            track.NotifyClipPixelsChanged();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
