using System.ComponentModel;
using DAW.MVVM.Models;

namespace DAW.Services;

/// <summary>
/// Service for managing edit tool states and communicating tool changes to different views.
/// Ensures only one edit tool is active at a time across the application.
/// </summary>
public sealed class ToolStateService : INotifyPropertyChanged
{
    private readonly GlobalApplicationState _globalState;

    public ToolStateService(GlobalApplicationState globalState)
    {
        _globalState = globalState ?? throw new ArgumentNullException(nameof(globalState));
        _globalState.PropertyChanged += OnGlobalStateChanged;
    }

    /// <summary>
    /// Currently active edit tool.
    /// </summary>
    public EditTool ActiveTool => _globalState.ActiveTool;

    /// <summary>
    /// Sets the active tool. This will affect all views (Playlist, Piano Roll, etc.).
    /// </summary>
    public void SetActiveTool(EditTool tool)
    {
        if (_globalState.ActiveTool == tool)
            return;

        var previousTool = _globalState.ActiveTool;
        _globalState.ActiveTool = tool;

        System.Diagnostics.Debug.WriteLine($"Tool changed: {previousTool} → {tool}");
        
        // Notify subscribers about tool change
        ToolChanged?.Invoke(this, new ToolChangedEventArgs(previousTool, tool));
    }

    /// <summary>
    /// Gets the cursor that should be used for the specified tool.
    /// </summary>
    public System.Windows.Input.Cursor GetToolCursor(EditTool tool)
    {
        return tool switch
        {
            EditTool.Select => System.Windows.Input.Cursors.Arrow,
            EditTool.Draw => System.Windows.Input.Cursors.Pen,
            EditTool.Paint => System.Windows.Input.Cursors.Cross,
            EditTool.Slice => CreateSliceCursor(),
            EditTool.Resize => System.Windows.Input.Cursors.SizeWE,
            EditTool.Zoom => CreateZoomCursor(),
            _ => System.Windows.Input.Cursors.Arrow
        };
    }

    /// <summary>
    /// Gets the tooltip text for the specified tool.
    /// </summary>
    public string GetToolTooltip(EditTool tool)
    {
        return tool switch
        {
            EditTool.Select => "Auswahl-Tool (S) - Objekte auswählen und verschieben",
            EditTool.Draw => "Zeichen-Tool (D) - Neue Noten/Clips erstellen", 
            EditTool.Paint => "Pinsel-Tool (P) - Mehrere Objekte schnell hinzufügen",
            EditTool.Slice => "Schnitt-Tool (C) - Clips und Noten teilen",
            EditTool.Resize => "Größe-Tool (R) - Objektlängen ändern",
            EditTool.Zoom => "Zoom-Tool (Z) - Bereich vergrößern",
            _ => "Unbekanntes Tool"
        };
    }

    /// <summary>
    /// Gets the keyboard shortcut key for the specified tool.
    /// </summary>
    public string GetToolShortcut(EditTool tool)
    {
        return tool switch
        {
            EditTool.Select => "S",
            EditTool.Draw => "D", 
            EditTool.Paint => "P",
            EditTool.Slice => "C",
            EditTool.Resize => "R",
            EditTool.Zoom => "Z",
            _ => ""
        };
    }

    /// <summary>
    /// Handles keyboard shortcuts for tool switching.
    /// </summary>
    public bool HandleKeyboardShortcut(string key)
    {
        var tool = key.ToUpper() switch
        {
            "S" => EditTool.Select,
            "D" => EditTool.Draw,
            "P" => EditTool.Paint,
            "C" => EditTool.Slice,
            "R" => EditTool.Resize,
            "Z" => EditTool.Zoom,
            _ => (EditTool?)null
        };

        if (tool.HasValue)
        {
            SetActiveTool(tool.Value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Event fired when the active tool changes.
    /// Views can subscribe to this to update their behavior.
    /// </summary>
    public event EventHandler<ToolChangedEventArgs>? ToolChanged;

    private void OnGlobalStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalApplicationState.ActiveTool))
        {
            OnPropertyChanged(nameof(ActiveTool));
        }
    }

    private static System.Windows.Input.Cursor CreateSliceCursor()
    {
        // In a real implementation, you'd create a custom cursor
        return System.Windows.Input.Cursors.Cross;
    }

    private static System.Windows.Input.Cursor CreateZoomCursor()
    {
        // In a real implementation, you'd create a zoom cursor with magnifying glass
        return System.Windows.Input.Cursors.Cross;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Event arguments for tool change notifications.
/// </summary>
public sealed class ToolChangedEventArgs : EventArgs
{
    public ToolChangedEventArgs(EditTool previousTool, EditTool newTool)
    {
        PreviousTool = previousTool;
        NewTool = newTool;
    }

    public EditTool PreviousTool { get; }
    public EditTool NewTool { get; }
}