using System.Windows.Input;

namespace DAW.Commands;

/// <summary>
/// Async implementation of ICommand for MVVM pattern
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Executes the async command and returns the task.
    /// </summary>
    public async Task ExecuteAsync()
    {
        if (!CanExecute(null))
            return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            
            await _execute();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Async command error: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }
}

/// <summary>
/// Generic async implementation of ICommand with typed parameter support
/// </summary>
public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        var typedParameter = parameter is T t ? t : default(T);
        return !_isExecuting && (_canExecute?.Invoke(typedParameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            var typedParameter = parameter is T t ? t : default(T);
            await _execute(typedParameter);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Executes the async command with parameter and returns the task.
    /// </summary>
    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            
            var typedParameter = parameter is T t ? t : default(T);
            await _execute(typedParameter);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Async command error: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }
}