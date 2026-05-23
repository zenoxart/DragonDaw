using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.Models;

namespace DAW.ViewModels;

public sealed class ArrangementViewModel : INotifyPropertyChanged
{
    public const double BasePixelsPerBeat  = 80.0;
    public const double DefaultTrackHeight = 52.0;
    public const double MinTrackHeight     = 16.0;
    public const double MaxTrackHeight     = 120.0;
    public const int    BeatsPerBar        = 4;

    private const double MinZoom   = 0.1;
    private const double MaxZoom   = 8.0;
    private const int    TotalBars = 256;

    private readonly MainViewModel _mainVm;
    private double _zoomLevel      = 1.0;
    private double _trackHeight    = DefaultTrackHeight;
    private double _playheadBeat;
    private double _snapResolution = 1.0;
    private ArrangementClipViewModel? _selectedClip;
    private ArrangementTrackViewModel? _selectedTrack;

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
            if (e.PropertyName == nameof(MainViewModel.IsPatternBrowserVisible))
                OnPropertyChanged(nameof(IsPatternBrowserVisible));
        };

        // Forward the toggle command from MainViewModel
        TogglePatternBrowserCommand = _mainVm.TogglePatternBrowserCommand;

        ZoomInCommand           = new RelayCommand(() => ZoomLevel = Math.Min(MaxZoom, ZoomLevel * 1.25));
        ZoomOutCommand          = new RelayCommand(() => ZoomLevel = Math.Max(MinZoom, ZoomLevel / 1.25));
        ZoomResetCommand        = new RelayCommand(() => ZoomLevel = 1.0);
        SnapOffCommand          = new RelayCommand(() => SnapResolution = 0.0);
        SnapQuarterCommand      = new RelayCommand(() => SnapResolution = 1.0);
        SnapEighthCommand       = new RelayCommand(() => SnapResolution = 0.5);
        SnapSixteenthCommand    = new RelayCommand(() => SnapResolution = 0.25);
        AddEmptyTrackCommand    = new RelayCommand(AddEmptyTrack);
        ResetTrackHeightCommand = new RelayCommand(() => CurrentTrackHeight = DefaultTrackHeight);
    }

    // ── Pattern browser visibility (forwarded from MainViewModel) ──────────────

    /// <summary>
    /// Whether the Patterns &amp; Channels sidebar is visible.
    /// Bound to the panel Visibility in ArrangementView.xaml.
    /// Toggled by the collapse button in the panel header and by View menu.
    /// </summary>
    public bool IsPatternBrowserVisible => _mainVm.IsPatternBrowserVisible;

    /// <summary>Command to toggle the pattern browser panel.</summary>
    public ICommand TogglePatternBrowserCommand { get; }

    // ── Sub-VMs ────────────────────────────────────────────────────────────────

    public ViewModels.Sequencer.PatternViewModel PatternVm => _mainVm.PatternVm;

    public void AddTrackFromChannel(ViewModels.Sequencer.ChannelViewModel ch)
    {
        _mainVm.AddEmptyTrack();
        var t = _mainVm.Tracks.LastOrDefault(); if (t != null) t.Title = ch.Name;
    }

    // ── Track collection ───────────────────────────────────────────────────────
    public ObservableCollection<ArrangementTrackViewModel> Tracks { get; }

    // ── Beat-to-pixel ──────────────────────────────────────────────────────────
    public double PixelsPerBeat   => BasePixelsPerBeat * ZoomLevel;
    public double BeatToPixel(double beat)  => Math.Round(beat * PixelsPerBeat);
    public double PixelToBeat(double pixel) => pixel / PixelsPerBeat;
    public double BeatsToPixels(double b)   => Math.Round(b * PixelsPerBeat);
    public double SnapToBeat(double beat)   => SnapResolution > 0 ? Math.Round(beat / SnapResolution) * SnapResolution : beat;

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
        set
        {
            if (!SetField(ref _snapResolution, value)) return;
            OnPropertyChanged(nameof(SnapDisplay)); OnPropertyChanged(nameof(IsSnapOff));
            OnPropertyChanged(nameof(IsSnapQuarter)); OnPropertyChanged(nameof(IsSnapEighth));
            OnPropertyChanged(nameof(IsSnapSixteenth)); OnPropertyChanged(nameof(SnapSelectedIndex));
        }
    }
    public string SnapDisplay  => _snapResolution switch { 0.0 => "OFF", 1.0 => "1/4", 0.5 => "1/8", 0.25 => "1/16", _ => $"{_snapResolution:F2}" };
    public bool IsSnapOff       => _snapResolution == 0.0;
    public bool IsSnapQuarter   => _snapResolution == 1.0;
    public bool IsSnapEighth    => _snapResolution == 0.5;
    public bool IsSnapSixteenth => _snapResolution == 0.25;

    public int SnapSelectedIndex
    {
        get => _snapResolution switch { 0.0 => 0, 1.0 => 1, 0.5 => 2, 0.25 => 3, 0.125 => 4, _ => 1 };
        set { SnapResolution = value switch { 0 => 0.0, 1 => 1.0, 2 => 0.5, 3 => 0.25, 4 => 0.125, _ => 1.0 }; OnPropertyChanged(nameof(SnapSelectedIndex)); }
    }

    // ── Playhead ───────────────────────────────────────────────────────────────
    public double PlayheadBeat
    {
        get => _playheadBeat;
        set { if (!SetField(ref _playheadBeat, Math.Max(0, value))) return; OnPropertyChanged(nameof(PlayheadPixel)); }
    }
    public double PlayheadPixel => Math.Round(BeatToPixel(PlayheadBeat));

    // ── Track height ──────────────────────────────────────────────────────────
    public double CurrentTrackHeight
    {
        get => _trackHeight;
        set { if (!SetField(ref _trackHeight, Math.Clamp(value, MinTrackHeight, MaxTrackHeight))) return; OnPropertyChanged(nameof(ClipHeight)); OnPropertyChanged(nameof(TotalTimelineHeight)); }
    }
    public double ClipHeight => Math.Max(8, CurrentTrackHeight - 4);

    // ── Layout ─────────────────────────────────────────────────────────────────
    public double TotalTimelineWidth  => TotalBars * BeatsPerBar * PixelsPerBeat;
    public double TotalTimelineHeight => Math.Max(Tracks.Count * CurrentTrackHeight, CurrentTrackHeight);
    public double BPM                 => _mainVm.BPM;

    // ── Selection ──────────────────────────────────────────────────────────────
    public ArrangementClipViewModel? SelectedClip
    {
        get => _selectedClip;
        set { if (_selectedClip != null) _selectedClip.Model.IsSelected = false; SetField(ref _selectedClip, value); if (_selectedClip != null) _selectedClip.Model.IsSelected = true; }
    }

    public ArrangementTrackViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set { ClearTrackSelection(); SetField(ref _selectedTrack, value); if (_selectedTrack != null) _selectedTrack.IsSelected = true; }
    }

    public void SelectTrack(ArrangementTrackViewModel track, bool addToSelection)
    {
        if (addToSelection)
        {
            track.IsSelected = !track.IsSelected;
            if (track.IsSelected) SetField(ref _selectedTrack, track);
            else if (_selectedTrack == track) SetField(ref _selectedTrack, Tracks.FirstOrDefault(t => t.IsSelected));
        }
        else SelectedTrack = track;
    }

    public IEnumerable<ArrangementTrackViewModel> GetSelectedTracks() => Tracks.Where(t => t.IsSelected);
    private void ClearTrackSelection() { foreach (var t in Tracks) t.IsSelected = false; }

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand ZoomInCommand           { get; }
    public ICommand ZoomOutCommand          { get; }
    public ICommand ZoomResetCommand        { get; }
    public ICommand SnapOffCommand          { get; }
    public ICommand SnapQuarterCommand      { get; }
    public ICommand SnapEighthCommand       { get; }
    public ICommand SnapSixteenthCommand    { get; }
    public ICommand AddEmptyTrackCommand    { get; }
    public ICommand ResetTrackHeightCommand { get; }

    // ── Internal ───────────────────────────────────────────────────────────────
    private void OnMainTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null) foreach (Track t in e.NewItems) Tracks.Add(new ArrangementTrackViewModel(t, this));
        if (e.OldItems != null) foreach (Track t in e.OldItems) { var vm = Tracks.FirstOrDefault(tv => tv.Model == t); if (vm != null) Tracks.Remove(vm); }
        OnPropertyChanged(nameof(TotalTimelineHeight));
    }

    public void RemoveTrack(ArrangementTrackViewModel trackVm) => _mainVm.Tracks.Remove(trackVm.Model);
    private void AddEmptyTrack()  => _mainVm.AddEmptyTrack();
    private void NotifyAllClipPixelsChanged() { foreach (var t in Tracks) t.NotifyClipPixelsChanged(); }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool SetField<T>(ref T f, T v, [CallerMemberName] string? n = null) { if (EqualityComparer<T>.Default.Equals(f, v)) return false; f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); return true; }
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
