using System.Windows.Input;
using DAW.ViewModels;

namespace DAW.Input;

/// <summary>
/// Manager for keyboard shortcuts and input gestures
/// </summary>
public class KeyboardShortcutManager
{
    private readonly FileMenuViewModel _fileMenuViewModel;
    private readonly Dictionary<KeyGesture, ICommand> _shortcuts;

    public KeyboardShortcutManager(FileMenuViewModel fileMenuViewModel)
    {
        _fileMenuViewModel = fileMenuViewModel ?? throw new ArgumentNullException(nameof(fileMenuViewModel));
        _shortcuts = new Dictionary<KeyGesture, ICommand>();
        RegisterShortcuts();
    }

    private void RegisterShortcuts()
    {
        // File menu shortcuts
        RegisterShortcut(new KeyGesture(Key.N, ModifierKeys.Control), _fileMenuViewModel.NewProjectCommand);
        RegisterShortcut(new KeyGesture(Key.O, ModifierKeys.Control), _fileMenuViewModel.OpenProjectCommand);
        RegisterShortcut(new KeyGesture(Key.S, ModifierKeys.Control), _fileMenuViewModel.SaveProjectCommand);
        RegisterShortcut(new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift), _fileMenuViewModel.SaveAsProjectCommand);
        RegisterShortcut(new KeyGesture(Key.F4, ModifierKeys.Alt), _fileMenuViewModel.ExitCommand);

        // Recent projects shortcuts (Alt+1 to Alt+0)
        for (int i = 1; i <= 9; i++)
        {
            var key = (Key)Enum.Parse(typeof(Key), $"D{i}");
            RegisterRecentProjectShortcut(new KeyGesture(key, ModifierKeys.Alt), i - 1);
        }
        // Alt+0 for 10th recent project
        RegisterRecentProjectShortcut(new KeyGesture(Key.D0, ModifierKeys.Alt), 9);
    }

    private void RegisterShortcut(KeyGesture gesture, ICommand command)
    {
        _shortcuts[gesture] = command;
    }

    private void RegisterRecentProjectShortcut(KeyGesture gesture, int index)
    {
        var command = new Commands.RelayCommand(() => ExecuteRecentProjectShortcut(index));
        _shortcuts[gesture] = command;
    }

    private void ExecuteRecentProjectShortcut(int index)
    {
        Task.Run(async () =>
        {
            var recentProjects = (await _fileMenuViewModel.GetRecentProjectsAsync()).ToList();
            if (index < recentProjects.Count)
            {
                var recentProject = recentProjects[index];
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (_fileMenuViewModel.OpenRecentCommand.CanExecute(recentProject))
                    {
                        _fileMenuViewModel.OpenRecentCommand.Execute(recentProject);
                    }
                });
            }
        });
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        var gesture = new KeyGesture(e.Key, Keyboard.Modifiers);
        
        if (_shortcuts.TryGetValue(gesture, out var command))
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    public IEnumerable<KeyBinding> GetKeyBindings()
    {
        foreach (var shortcut in _shortcuts)
        {
            yield return new KeyBinding(shortcut.Value, shortcut.Key);
        }
    }
}