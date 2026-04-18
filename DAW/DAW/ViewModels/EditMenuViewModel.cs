using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.Models;

namespace DAW.ViewModels;

/// <summary>
/// Represents a single undoable action.
/// </summary>
public sealed class UndoAction
{
    public required string Description { get; init; }
    public required Action Undo { get; init; }
    public required Action Redo { get; init; }
}

/// <summary>
/// ViewModel for the Bearbeiten (Edit) menu.
/// Owns the undo/redo stack and clipboard for arrangement clips.
/// </summary>
public sealed class EditMenuViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _mainVm;

    // Undo / Redo stacks
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();

    // Clipboard (stores a snapshot of a clip that was copied/cut)
    private ArrangementClip? _clipboardClip;
    private ArrangementTrackViewModel? _clipboardSourceTrack;

    public EditMenuViewModel(MainViewModel mainVm)
    {
        _mainVm = mainVm;

        UndoCommand   = new RelayCommand(ExecuteUndo, () => _undoStack.Count > 0);
        RedoCommand   = new RelayCommand(ExecuteRedo, () => _redoStack.Count > 0);
        CutCommand    = new RelayCommand(ExecuteCut, HasSelectedClip);
        CopyCommand   = new RelayCommand(ExecuteCopy, HasSelectedClip);
        PasteCommand  = new RelayCommand(ExecutePaste, () => _clipboardClip is not null);
        DeleteCommand = new RelayCommand(ExecuteDelete, HasSelectedClip);
        SelectAllCommand = new RelayCommand(ExecuteSelectAll);

        BuildMenu();
    }

    // ── Commands ────────────────────────────────────────────────────────────
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SelectAllCommand { get; }

    public ObservableCollection<MenuItemViewModel> EditMenuItems { get; } = [];

    // ── Public API for other parts of the app to push undoable actions ──────
    public void PushUndoAction(UndoAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        InvalidateCommands();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private bool HasSelectedClip() => _mainVm.ArrangementVm.SelectedClip is not null;

    private ArrangementTrackViewModel? FindTrackForClip(ArrangementClipViewModel clipVm)
    {
        return _mainVm.ArrangementVm.Tracks.FirstOrDefault(t => t.Clips.Contains(clipVm));
    }

    // ── Undo / Redo ─────────────────────────────────────────────────────────

    private void ExecuteUndo()
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
        InvalidateCommands();
    }

    private void ExecuteRedo()
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack.Pop();
        action.Redo();
        _undoStack.Push(action);
        InvalidateCommands();
    }

    // ── Cut ─────────────────────────────────────────────────────────────────

    private void ExecuteCut()
    {
        var clipVm = _mainVm.ArrangementVm.SelectedClip;
        if (clipVm is null) return;

        var track = FindTrackForClip(clipVm);
        if (track is null) return;

        // Snapshot for undo
        var snapshot = CloneClip(clipVm.Model);
        var capturedTrack = track;
        var capturedClipVm = clipVm;

        // Store in clipboard
        _clipboardClip = snapshot;
        _clipboardSourceTrack = track;

        // Remove from arrangement
        track.RemoveClip(clipVm);
        _mainVm.ArrangementVm.SelectedClip = null;

        PushUndoAction(new UndoAction
        {
            Description = $"Ausschneiden: {snapshot.DisplayName}",
            Undo = () =>
            {
                var restored = new ArrangementClipViewModel(CloneClip(snapshot), _mainVm.ArrangementVm);
                capturedTrack.Clips.Add(restored);
                _mainVm.ArrangementVm.SelectedClip = restored;
            },
            Redo = () =>
            {
                var toRemove = capturedTrack.Clips.FirstOrDefault(
                    c => Math.Abs(c.Model.StartBeat - snapshot.StartBeat) < 0.001
                      && c.Model.DisplayName == snapshot.DisplayName);
                if (toRemove is not null)
                {
                    capturedTrack.RemoveClip(toRemove);
                    _mainVm.ArrangementVm.SelectedClip = null;
                }
            }
        });

        _mainVm.StatusMessage = $"✂ Ausgeschnitten: {snapshot.DisplayName}";
        InvalidateCommands();
    }

    // ── Copy ────────────────────────────────────────────────────────────────

    private void ExecuteCopy()
    {
        var clipVm = _mainVm.ArrangementVm.SelectedClip;
        if (clipVm is null) return;

        _clipboardClip = CloneClip(clipVm.Model);
        _clipboardSourceTrack = FindTrackForClip(clipVm);

        _mainVm.StatusMessage = $"📋 Kopiert: {clipVm.Model.DisplayName}";
        InvalidateCommands();
    }

    // ── Paste ───────────────────────────────────────────────────────────────

    private void ExecutePaste()
    {
        if (_clipboardClip is null) return;

        // Paste into the same track, offset by 4 beats from playhead
        var targetTrack = _clipboardSourceTrack
            ?? _mainVm.ArrangementVm.Tracks.FirstOrDefault();
        if (targetTrack is null) return;

        var pastedClip = CloneClip(_clipboardClip);
        pastedClip.StartBeat = _mainVm.ArrangementVm.PlayheadBeat;

        var pastedVm = new ArrangementClipViewModel(pastedClip, _mainVm.ArrangementVm);
        var capturedTrack = targetTrack;

        targetTrack.Clips.Add(pastedVm);
        _mainVm.ArrangementVm.SelectedClip = pastedVm;

        PushUndoAction(new UndoAction
        {
            Description = $"Einfügen: {pastedClip.DisplayName}",
            Undo = () =>
            {
                capturedTrack.RemoveClip(pastedVm);
                _mainVm.ArrangementVm.SelectedClip = null;
            },
            Redo = () =>
            {
                capturedTrack.Clips.Add(pastedVm);
                _mainVm.ArrangementVm.SelectedClip = pastedVm;
            }
        });

        _mainVm.StatusMessage = $"📄 Eingefügt: {pastedClip.DisplayName}";
    }

    // ── Delete ──────────────────────────────────────────────────────────────

    private void ExecuteDelete()
    {
        var clipVm = _mainVm.ArrangementVm.SelectedClip;
        if (clipVm is null) return;

        var track = FindTrackForClip(clipVm);
        if (track is null) return;

        var snapshot = CloneClip(clipVm.Model);
        var capturedTrack = track;

        track.RemoveClip(clipVm);
        _mainVm.ArrangementVm.SelectedClip = null;

        PushUndoAction(new UndoAction
        {
            Description = $"Löschen: {snapshot.DisplayName}",
            Undo = () =>
            {
                var restored = new ArrangementClipViewModel(CloneClip(snapshot), _mainVm.ArrangementVm);
                capturedTrack.Clips.Add(restored);
                _mainVm.ArrangementVm.SelectedClip = restored;
            },
            Redo = () =>
            {
                var toRemove = capturedTrack.Clips.FirstOrDefault(
                    c => Math.Abs(c.Model.StartBeat - snapshot.StartBeat) < 0.001
                      && c.Model.DisplayName == snapshot.DisplayName);
                if (toRemove is not null)
                {
                    capturedTrack.RemoveClip(toRemove);
                    _mainVm.ArrangementVm.SelectedClip = null;
                }
            }
        });

        _mainVm.StatusMessage = $"🗑 Gelöscht: {snapshot.DisplayName}";
    }

    // ── Select All ──────────────────────────────────────────────────────────

    private void ExecuteSelectAll()
    {
        // Select the first clip if none selected (basic implementation)
        var firstClip = _mainVm.ArrangementVm.Tracks
            .SelectMany(t => t.Clips)
            .FirstOrDefault();
        if (firstClip is not null)
            _mainVm.ArrangementVm.SelectedClip = firstClip;
    }

    // ── Clone helper ────────────────────────────────────────────────────────

    private static ArrangementClip CloneClip(ArrangementClip source) => new()
    {
        DisplayName    = source.DisplayName,
        StartBeat      = source.StartBeat,
        LengthInBeats  = source.LengthInBeats,
        Color          = source.Color,
        SourceFilePath = source.SourceFilePath,
        WaveformData   = source.WaveformData is { Length: > 0 } ? [.. source.WaveformData] : [],
        AudioDuration  = source.AudioDuration
    };

    // ── Menu building ───────────────────────────────────────────────────────

    private void BuildMenu()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            EditMenuItems.Clear();

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Rückgängig",
                Command = UndoCommand,
                Icon = "↶",
                InputGestureText = "Ctrl+Z"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Wiederholen",
                Command = RedoCommand,
                Icon = "↷",
                InputGestureText = "Ctrl+Y"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel { IsSeparator = true }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Ausschneiden",
                Command = CutCommand,
                Icon = "✂",
                InputGestureText = "Ctrl+X"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Kopieren",
                Command = CopyCommand,
                Icon = "📋",
                InputGestureText = "Ctrl+C"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Einfügen",
                Command = PasteCommand,
                Icon = "📄",
                InputGestureText = "Ctrl+V"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel { IsSeparator = true }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Löschen",
                Command = DeleteCommand,
                Icon = "🗑",
                InputGestureText = "Del"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Alles auswählen",
                Command = SelectAllCommand,
                Icon = "☐",
                InputGestureText = "Ctrl+A"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel { IsSeparator = true }));

            // ── Edit Tools ──
            var toolbar = _mainVm.GlobalToolbar;

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Auswahl-Tool",
                Command = toolbar.SelectToolCommand,
                Icon = "📍",
                InputGestureText = "S"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Zeichen-Tool",
                Command = toolbar.DrawToolCommand,
                Icon = "✏",
                InputGestureText = "D"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Pinsel-Tool",
                Command = toolbar.PaintToolCommand,
                Icon = "🖌",
                InputGestureText = "P"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Schnitt-Tool",
                Command = toolbar.SliceToolCommand,
                Icon = "✂",
                InputGestureText = "C"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Größe-Tool",
                Command = toolbar.ResizeToolCommand,
                Icon = "↔",
                InputGestureText = "R"
            }));

            EditMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Zoom-Tool",
                Command = toolbar.ZoomToolCommand,
                Icon = "🔍",
                InputGestureText = "Z"
            }));
        });
    }

    private void InvalidateCommands()
    {
        CommandManager.InvalidateRequerySuggested();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
