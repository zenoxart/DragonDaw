using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DAW.Models;

namespace DAW.ViewModels;

/// <summary>
/// ViewModel for a single clip placed on the arrangement timeline.
///
/// Beat-to-Pixel formula (canvas-absolute, ScrollViewer handles viewport offset):
///   PixelLeft  = StartBeat    × PixelsPerBeat
///   PixelWidth = LengthInBeats × PixelsPerBeat
///
/// When the zoom level changes, ArrangementViewModel calls NotifyPixelsChanged()
/// on every clip to push fresh pixel values without requiring an event subscription.
/// </summary>
public sealed class ArrangementClipViewModel : INotifyPropertyChanged
{
    private readonly ArrangementClip _model;

    public ArrangementClipViewModel(ArrangementClip model, ArrangementViewModel arrangement)
    {
        _model = model;
        Arrangement = arrangement;
        _model.PropertyChanged += OnModelPropertyChanged;
    }

    public ArrangementClip Model => _model;

    /// <summary>Parent arrangement — used by ClipControl for PixelToBeat and SnapToBeat.</summary>
    public ArrangementViewModel Arrangement { get; }

    // ── Pixel-space properties (derived, re-evaluated on zoom or beat change) ─────

    /// <summary>Canvas-absolute left edge in pixels with pixel snapping.</summary>
    public double PixelLeft => Math.Round(_model.StartBeat * Arrangement.PixelsPerBeat);

    /// <summary>Canvas-absolute width in pixels with pixel snapping.</summary>
    public double PixelWidth => Math.Round(_model.LengthInBeats * Arrangement.PixelsPerBeat);

    // ── Forwarded model properties ─────────────────────────────────────────────

    public string DisplayName => _model.DisplayName;
    public bool   IsSelected  => _model.IsSelected;
    public bool   IsMuted     => _model.IsMuted;
    public Color  Color       => _model.Color;
    public double[]? WaveformData => _model.WaveformData;
    public bool IsAudioClip => _model.IsAudioClip;

    public Brush ClipFill => new SolidColorBrush(Color.FromArgb(
        IsMuted ? (byte)60 : (byte)175,
        _model.Color.R, _model.Color.G, _model.Color.B));

    public Brush ClipBorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(212, 175, 55))   // gold selection
        : new SolidColorBrush(Color.FromArgb(220,
              (byte)Math.Min(255, _model.Color.R + 40),
              (byte)Math.Min(255, _model.Color.G + 40),
              (byte)Math.Min(255, _model.Color.B + 40)));

    /// <summary>Waveform overlay color (semi-transparent white).</summary>
    public Brush WaveformBrush => new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

    // ── Called by ArrangementViewModel when zoom changes ──────────────────────

    public void NotifyPixelsChanged()
    {
        OnPropertyChanged(nameof(PixelLeft));
        OnPropertyChanged(nameof(PixelWidth));
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ArrangementClip.StartBeat):
                OnPropertyChanged(nameof(PixelLeft));
                break;
            case nameof(ArrangementClip.LengthInBeats):
                OnPropertyChanged(nameof(PixelWidth));
                break;
            case nameof(ArrangementClip.DisplayName):
                OnPropertyChanged(nameof(DisplayName));
                break;
            case nameof(ArrangementClip.IsSelected):
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(ClipBorderBrush));
                break;
            case nameof(ArrangementClip.IsMuted):
                OnPropertyChanged(nameof(IsMuted));
                OnPropertyChanged(nameof(ClipFill));
                break;
            case nameof(ArrangementClip.Color):
                OnPropertyChanged(nameof(Color));
                OnPropertyChanged(nameof(ClipFill));
                OnPropertyChanged(nameof(ClipBorderBrush));
                break;
            case nameof(ArrangementClip.WaveformData):
                OnPropertyChanged(nameof(WaveformData));
                OnPropertyChanged(nameof(IsAudioClip));
                break;
            case nameof(ArrangementClip.SourceFilePath):
                OnPropertyChanged(nameof(IsAudioClip));
                break;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
