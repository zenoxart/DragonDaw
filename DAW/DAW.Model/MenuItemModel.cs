using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DAW.MVVM.Models;

/// <summary>
/// Model representing a menu item with hierarchical structure support
/// </summary>
public class MenuItemModel
{
    public string Header { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public string? InputGestureText { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsEnabled { get; set; } = true;
    public ObservableCollection<MenuItemModel>? Children { get; set; }
    public MenuItemRole Role { get; set; } = MenuItemRole.Normal;
}

public enum MenuItemRole
{
    Normal,
    RecentProject,
    Template
}