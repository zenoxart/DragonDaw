using System.IO;

namespace DAW.Models;

/// <summary>
/// Represents a file node in the Audio Browser tree.
/// Classifies audio, MIDI, and other files automatically from their extension.
/// </summary>
public sealed class AudioBrowserFile : AudioBrowserItem
{
    private static readonly HashSet<string> AudioExts = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".flac", ".ogg", ".aiff", ".aif", ".m4a", ".wma" };

    private static readonly HashSet<string> MidiExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mid", ".midi" };

    public AudioBrowserFile(string name, string fullPath)
        : base(name, fullPath, Classify(fullPath))
    {
        Extension = Path.GetExtension(fullPath);
    }

    public string    Extension { get; }
    public long      FileSize  { get; init; }
    public TimeSpan? Duration  { get; set; }

    public static bool IsAudioExtension(string ext)     => AudioExts.Contains(ext);
    public static bool IsSupportedExtension(string ext) => AudioExts.Contains(ext) || MidiExts.Contains(ext);

    private static AudioBrowserItemType Classify(string path)
    {
        var ext = Path.GetExtension(path);
        if (AudioExts.Contains(ext)) return AudioBrowserItemType.AudioFile;
        if (MidiExts.Contains(ext))  return AudioBrowserItemType.MidiFile;
        return AudioBrowserItemType.OtherFile;
    }
}
