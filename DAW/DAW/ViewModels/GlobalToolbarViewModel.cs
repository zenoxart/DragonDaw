using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.Models;
using DAW.Services;

namespace DAW.ViewModels;

/// <summary>
/// ViewModel for the global toolbar containing transport controls and edit tools.
/// Communicates with services to ensure thread-safe audio engine interaction.
/// </summary>
public sealed class GlobalToolbarViewModel : INotifyPropertyChanged
{
    private readonly GlobalApplicationState _globalState;
    private readonly TransportService _transportService;
    private readonly ToolStateService _toolStateService;

    public GlobalToolbarViewModel(
        GlobalApplicationState globalState,
        TransportService transportService, 
        ToolStateService toolStateService)
    {
        _globalState = globalState ?? throw new ArgumentNullException(nameof(globalState));
        _transportService = transportService ?? throw new ArgumentNullException(nameof(transportService));
        _toolStateService = toolStateService ?? throw new ArgumentNullException(nameof(toolStateService));

        // Initialize commands
        InitializeTransportCommands();
        InitializeEditCommands();
        InitializeToolCommands();
        InitializeToggleCommands();

        // Subscribe to state changes
        _globalState.PropertyChanged += OnGlobalStateChanged;
        _toolStateService.PropertyChanged += OnToolStateChanged;

        // Initialize tool items for UI binding
        InitializeToolItems();
    }

    #region Transport Commands

    public ICommand PlayCommand { get; private set; } = null!;
    public ICommand PauseCommand { get; private set; } = null!;
    public ICommand StopCommand { get; private set; } = null!;
    public ICommand RecordCommand { get; private set; } = null!;

    #endregion

    #region Edit Commands

    public ICommand UndoCommand { get; private set; } = null!;
    public ICommand RedoCommand { get; private set; } = null!;
    public ICommand CutCommand { get; private set; } = null!;
    public ICommand CopyCommand { get; private set; } = null!;
    public ICommand PasteCommand { get; private set; } = null!;
    public ICommand DeleteCommand { get; private set; } = null!;

    #endregion

    #region Tool Commands

    public ICommand SelectToolCommand { get; private set; } = null!;
    public ICommand DrawToolCommand { get; private set; } = null!;
    public ICommand PaintToolCommand { get; private set; } = null!;
    public ICommand SliceToolCommand { get; private set; } = null!;
    public ICommand ResizeToolCommand { get; private set; } = null!;
    public ICommand ZoomToolCommand { get; private set; } = null!;

    #endregion

    #region Toggle Commands

    public ICommand ToggleMonitoringCommand { get; private set; } = null!;
    public ICommand ToggleMetronomeCommand { get; private set; } = null!;

    #endregion

    #region State Properties

    /// <summary>Transport state properties for UI binding.</summary>
    public bool IsPlaying => _globalState.IsPlaying;
    public bool IsPaused => _globalState.IsPaused;
    public bool IsStopped => _globalState.IsStopped;
    public bool IsRecording => _globalState.IsRecording;

    /// <summary>Toggle state properties.</summary>
    public bool IsMonitoringEnabled => _globalState.IsMonitoringEnabled;
    public bool IsMetronomeEnabled => _globalState.IsMetronomeEnabled;

    /// <summary>Tool state properties.</summary>
    public bool IsSelectToolActive => _globalState.ActiveTool == EditTool.Select;
    public bool IsDrawToolActive => _globalState.ActiveTool == EditTool.Draw;
    public bool IsPaintToolActive => _globalState.ActiveTool == EditTool.Paint;
    public bool IsSliceToolActive => _globalState.ActiveTool == EditTool.Slice;
    public bool IsResizeToolActive => _globalState.ActiveTool == EditTool.Resize;
    public bool IsZoomToolActive => _globalState.ActiveTool == EditTool.Zoom;

    /// <summary>Current playback position for display.</summary>
    public TimeSpan CurrentPosition => _globalState.CurrentPosition;

    /// <summary>Current BPM for display.</summary>
    public double BPM => _globalState.BPM;

    /// <summary>Formatted position string for UI display.</summary>
    public string PositionDisplay => FormatTimeSpan(_globalState.CurrentPosition);

    #endregion

    #region Tool Items for UI

    /// <summary>
    /// Collection of tool items for binding to UI (for toolbar buttons or menu items).
    /// </summary>
    public ObservableCollection<ToolItemViewModel> ToolItems { get; } = [];

    #endregion

    private void InitializeTransportCommands()
    {
        PlayCommand = new AsyncRelayCommand(
            async () => await _transportService.PlayAsync(),
            () => !IsPlaying);

        PauseCommand = new AsyncRelayCommand(
            async () => await _transportService.PauseAsync(),
            () => IsPlaying);

        StopCommand = new AsyncRelayCommand(
            async () => await _transportService.StopAsync(),
            () => !IsStopped);

        RecordCommand = new AsyncRelayCommand(
            async () => await _transportService.RecordAsync(),
            () => !IsRecording);
    }

    private void InitializeEditCommands()
    {
        UndoCommand = new RelayCommand(
            () => System.Diagnostics.Debug.WriteLine("Undo executed"),
            () => true); // TODO: Implement actual undo/redo system

        RedoCommand = new RelayCommand(
            () => System.Diagnostics.Debug.WriteLine("Redo executed"),
            () => true);

        CutCommand = new RelayCommand(
            () => System.Diagnostics.Debug.WriteLine("Cut executed"),
            () => true);

        CopyCommand = new RelayCommand(
            () => System.Diagnostics.Debug.WriteLine("Copy executed"),
            () => true);

        PasteCommand = new RelayCommand(
            () => System.Diagnostics.Debug.WriteLine("Paste executed"),
            () => true);

        DeleteCommand = new RelayCommand(
            () => System.Diagnostics.Debug.WriteLine("Delete executed"),
            () => true);
    }

    private void InitializeToolCommands()
    {
        SelectToolCommand = new RelayCommand(() => _toolStateService.SetActiveTool(EditTool.Select));
        DrawToolCommand = new RelayCommand(() => _toolStateService.SetActiveTool(EditTool.Draw));
        PaintToolCommand = new RelayCommand(() => _toolStateService.SetActiveTool(EditTool.Paint));
        SliceToolCommand = new RelayCommand(() => _toolStateService.SetActiveTool(EditTool.Slice));
        ResizeToolCommand = new RelayCommand(() => _toolStateService.SetActiveTool(EditTool.Resize));
        ZoomToolCommand = new RelayCommand(() => _toolStateService.SetActiveTool(EditTool.Zoom));
    }

    private void InitializeToggleCommands()
    {
        ToggleMonitoringCommand = new RelayCommand(
            () => _transportService.SetMonitoring(!_globalState.IsMonitoringEnabled));

        ToggleMetronomeCommand = new RelayCommand(
            () => _transportService.SetMetronome(!_globalState.IsMetronomeEnabled));
    }

    private void InitializeToolItems()
    {
        var tools = Enum.GetValues<EditTool>();
        foreach (var tool in tools)
        {
            ToolItems.Add(new ToolItemViewModel(tool, _toolStateService, GetToolCommand(tool)));
        }
    }

    private ICommand GetToolCommand(EditTool tool)
    {
        return tool switch
        {
            EditTool.Select => SelectToolCommand,
            EditTool.Draw => DrawToolCommand,
            EditTool.Paint => PaintToolCommand,
            EditTool.Slice => SliceToolCommand,
            EditTool.Resize => ResizeToolCommand,
            EditTool.Zoom => ZoomToolCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };
    }

    private void OnGlobalStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GlobalApplicationState.TransportState):
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(IsRecording));
                InvalidateTransportCommands();
                break;

            case nameof(GlobalApplicationState.IsMonitoringEnabled):
                OnPropertyChanged(nameof(IsMonitoringEnabled));
                break;

            case nameof(GlobalApplicationState.IsMetronomeEnabled):
                OnPropertyChanged(nameof(IsMetronomeEnabled));
                break;

            case nameof(GlobalApplicationState.CurrentPosition):
                OnPropertyChanged(nameof(CurrentPosition));
                OnPropertyChanged(nameof(PositionDisplay));
                break;

            case nameof(GlobalApplicationState.BPM):
                OnPropertyChanged(nameof(BPM));
                break;

            case nameof(GlobalApplicationState.ActiveTool):
                NotifyAllToolStates();
                break;
        }
    }

    private void OnToolStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolStateService.ActiveTool))
        {
            NotifyAllToolStates();
        }
    }

    private void NotifyAllToolStates()
    {
        OnPropertyChanged(nameof(IsSelectToolActive));
        OnPropertyChanged(nameof(IsDrawToolActive));
        OnPropertyChanged(nameof(IsPaintToolActive));
        OnPropertyChanged(nameof(IsSliceToolActive));
        OnPropertyChanged(nameof(IsResizeToolActive));
        OnPropertyChanged(nameof(IsZoomToolActive));
    }

    private void InvalidateTransportCommands()
    {
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        return $"{(int)timeSpan.TotalMinutes:D2}:{timeSpan.Seconds:D2}.{timeSpan.Milliseconds / 10:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// ViewModel for individual tool items in the toolbar.
/// </summary>
public sealed class ToolItemViewModel : INotifyPropertyChanged
{
    private readonly EditTool _tool;
    private readonly ToolStateService _toolStateService;

    public ToolItemViewModel(EditTool tool, ToolStateService toolStateService, ICommand command)
    {
        _tool = tool;
        _toolStateService = toolStateService ?? throw new ArgumentNullException(nameof(toolStateService));
        Command = command ?? throw new ArgumentNullException(nameof(command));

        _toolStateService.PropertyChanged += OnToolStateChanged;
    }

    public EditTool Tool => _tool;
    public ICommand Command { get; }
    public string Name => _tool.ToString();
    public string Tooltip => _toolStateService.GetToolTooltip(_tool);
    public string Shortcut => _toolStateService.GetToolShortcut(_tool);
    public bool IsActive => _toolStateService.ActiveTool == _tool;

    private void OnToolStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolStateService.ActiveTool))
        {
            OnPropertyChanged(nameof(IsActive));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}