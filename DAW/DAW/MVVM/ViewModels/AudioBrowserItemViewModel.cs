using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// Abstract base for all Audio Browser tree node ViewModels.
/// Carries name/path/depth and selection state; concrete subclasses
/// add folder-specific (lazy loading) or file-specific (preview) behaviour.
/// </summary>
public abstract class AudioBrowserItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    protected AudioBrowserItemViewModel(string name, string fullPath, int depth)
    {
        Name     = name;
        FullPath = fullPath;
        Depth    = depth;
    }

    public string Name     { get; }
    public string FullPath { get; }
    public int    Depth    { get; }

    /// <summary>Left-margin pixel value used by the flat-list indentation approach.</summary>
    public double Indentation => Depth * 14.0;

    public abstract bool IsFolder { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
