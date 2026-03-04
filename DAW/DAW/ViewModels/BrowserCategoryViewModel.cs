using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;

namespace DAW.ViewModels;

public enum BrowserCategoryType { Audio, Computer, Documents, Favorites }

/// <summary>
/// Represents one icon-button in the horizontal category strip
/// (Audio · Computer · Documents · Favorites).
/// </summary>
public sealed class BrowserCategoryViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public BrowserCategoryViewModel(
        BrowserCategoryType type,
        string icon,
        string tooltip,
        Action<BrowserCategoryViewModel> onSelect)
    {
        CategoryType  = type;
        Icon          = icon;
        Tooltip       = tooltip;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }

    public BrowserCategoryType CategoryType { get; }
    public string              Icon         { get; }
    public string              Tooltip      { get; }
    public ICommand            SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
