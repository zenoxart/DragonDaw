using System.Windows.Input;
using DAW.Commands;
using DAW.Models;
using DAW.Services;

namespace DAW.ViewModels;

/// <summary>
/// ViewModel for an audio or MIDI file in the Audio Browser tree.
///
/// Preview behaviour
/// ──────────────────
/// • Single click  → <see cref="AudioBrowserViewModel"/> calls
///   <see cref="AudioPreviewService.PreviewAsync"/> (handled in parent VM).
/// • <see cref="IsPreviewing"/> is set by the parent VM via the
///   <see cref="AudioPreviewService.StateChanged"/> event so that the
///   View can highlight the currently playing row.
/// </summary>
public sealed class AudioBrowserFileViewModel : AudioBrowserItemViewModel
{
    private bool _isPreviewing;

    public AudioBrowserFileViewModel(AudioBrowserFile model, int depth)
        : base(model.Name, model.FullPath, depth)
    {
        Model = model;

        // Fire-and-forget preview command; exceptions are swallowed inside
        // StartPreviewAsync so the command binder never sees them.
        PreviewCommand = new RelayCommand(
            () => _ = StartPreviewAsync(),
            () => model.ItemType == AudioBrowserItemType.AudioFile);
    }

    public AudioBrowserFile Model { get; }

    public override bool IsFolder => false;
    public bool IsAudio => Model.ItemType == AudioBrowserItemType.AudioFile;
    public bool IsMidi  => Model.ItemType == AudioBrowserItemType.MidiFile;

    public string Extension => Model.Extension;

    /// <summary>Unicode icon rendered in the tree row.</summary>
    public string FileIcon => IsAudio ? "🎵" : IsMidi ? "🎹" : "📄";

    /// <summary>
    /// True while this file's preview is playing.
    /// Set externally by <see cref="AudioBrowserViewModel"/>.
    /// </summary>
    public bool IsPreviewing
    {
        get => _isPreviewing;
        internal set { if (_isPreviewing == value) return; _isPreviewing = value; OnPropertyChanged(); }
    }

    public ICommand PreviewCommand { get; }

    private async Task StartPreviewAsync()
    {
        try
        {
            await AudioPreviewService.Instance.PreviewAsync(FullPath);
        }
        catch { /* preview errors are non-critical */ }
    }
}
