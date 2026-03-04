namespace DAW.Models;

public enum AudioBrowserItemType { Folder, AudioFile, MidiFile, OtherFile }

/// <summary>
/// Abstract base for all nodes in the Audio Browser tree.
/// </summary>
public abstract class AudioBrowserItem
{
    protected AudioBrowserItem(string name, string fullPath, AudioBrowserItemType type)
    {
        Name     = name;
        FullPath = fullPath;
        ItemType = type;
    }

    public string             Name     { get; }
    public string             FullPath { get; }
    public AudioBrowserItemType ItemType { get; }
}
