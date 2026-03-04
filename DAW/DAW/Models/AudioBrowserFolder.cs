namespace DAW.Models;

/// <summary>
/// Represents a directory node in the Audio Browser tree.
/// </summary>
public sealed class AudioBrowserFolder : AudioBrowserItem
{
    public AudioBrowserFolder(string name, string fullPath)
        : base(name, fullPath, AudioBrowserItemType.Folder) { }

    /// <summary>
    /// Whether this folder contains sub-folders or supported audio files.
    /// Used to decide whether to show an expand arrow before the children are loaded.
    /// </summary>
    public bool HasSubItems { get; init; }
}
