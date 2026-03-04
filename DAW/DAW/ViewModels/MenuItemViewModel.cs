using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Models;

namespace DAW.ViewModels;

/// <summary>
/// ViewModel for menu items with hierarchical data binding support
/// </summary>
public class MenuItemViewModel : INotifyPropertyChanged
{
    private bool _isEnabled = true;
    private bool _isChecked;
    private string _header = string.Empty;
    private string? _icon;
    private string? _inputGestureText;

    public MenuItemViewModel(MenuItemModel model)
    {
        Header = model.Header;
        Icon = model.Icon;
        Command = model.Command;
        CommandParameter = model.CommandParameter;
        InputGestureText = model.InputGestureText;
        IsSeparator = model.IsSeparator;
        Role = model.Role;

        if (model.Children != null)
        {
            Children = new ObservableCollection<MenuItemViewModel>(
                model.Children.Select(child => new MenuItemViewModel(child))
            );
        }
    }

    public string Header
    {
        get => _header;
        set => SetProperty(ref _header, value);
    }

    public string? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }

    public string? InputGestureText
    {
        get => _inputGestureText;
        set => SetProperty(ref _inputGestureText, value);
    }

    public bool IsSeparator { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public MenuItemRole Role { get; set; }

    public ObservableCollection<MenuItemViewModel>? Children { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}