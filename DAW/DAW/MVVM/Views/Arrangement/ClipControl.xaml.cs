using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DAW.MVVM.ViewModels;

namespace DAW.MVVM.Views.Arrangement;

/// <summary>
/// Interactive clip tile on the arrangement timeline.
///
/// Event flow
/// ──────────
/// MouseDown   → select clip, begin drag (Move) or resize (right 7 px = Resize)
/// MouseMove   → update Model.StartBeat (Move) or Model.LengthInBeats (Resize)
///               using screen-coordinate delta so scroll offset is irrelevant
/// MouseUp     → release capture, commit position
/// RightClick  → context menu (Mute toggle, Delete)
///
/// Drag is performed in screen-coordinate delta space:
///   deltaBeats = (screenX_current − screenX_start) / PixelsPerBeat
/// This cancels out any scroll offset because only the delta matters.
/// </summary>
public partial class ClipControl : UserControl
{
    private enum DragMode { None, Move, Resize }

    private DragMode _dragMode;
    private Point    _dragStartScreen;
    private double   _originalStartBeat;
    private double   _originalLength;

    public ClipControl()
    {
        InitializeComponent();
    }

    private ArrangementClipViewModel? ClipVm => DataContext as ArrangementClipViewModel;

    // ── Mouse interaction ──────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (ClipVm is not { } vm) return;

        // Select this clip in the arrangement
        vm.Arrangement.SelectedClip = vm;

        _dragStartScreen    = e.GetPosition(null);
        _originalStartBeat  = vm.Model.StartBeat;
        _originalLength     = vm.Model.LengthInBeats;

        var posInClip = e.GetPosition(this);
        _dragMode = posInClip.X >= ActualWidth - 8 ? DragMode.Resize : DragMode.Move;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ClipVm is not { } vm) return;

        if (_dragMode == DragMode.None)
        {
            // Update hover cursor
            var pos = e.GetPosition(this);
            Cursor = pos.X >= ActualWidth - 8 ? Cursors.SizeWE : Cursors.SizeAll;
            return;
        }

        // Screen-coordinate delta — scroll offset cancels out automatically
        var current     = e.GetPosition(null);
        var deltaPixels = current.X - _dragStartScreen.X;
        var deltaBeats  = deltaPixels / vm.Arrangement.PixelsPerBeat;

        if (_dragMode == DragMode.Move)
        {
            vm.Model.StartBeat = vm.Arrangement.SnapToBeat(
                Math.Max(0.0, _originalStartBeat + deltaBeats));
        }
        else // Resize
        {
            vm.Model.LengthInBeats = vm.Arrangement.SnapToBeat(
                Math.Max(0.25, _originalLength + deltaBeats));
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode == DragMode.None) return;

        _dragMode = DragMode.None;
        Cursor    = Cursors.SizeAll;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragMode == DragMode.None)
            Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (ClipVm is not { } vm) return;

        var menu = new ContextMenu();

        var muteItem = new MenuItem
        {
            Header = vm.IsMuted ? "🔊 Clip einschalten" : "🔇 Clip muten"
        };
        muteItem.Click += (_, _) => vm.Model.IsMuted = !vm.Model.IsMuted;

        var deleteItem = new MenuItem { Header = "🗑 Clip löschen" };
        deleteItem.Click += (_, _) =>
        {
            var trackVm = FindAncestorDataContext<ArrangementTrackViewModel>(this);
            trackVm?.RemoveClip(vm);
        };

        menu.Items.Add(muteItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);

        ContextMenu = menu;
        // Let WPF open it automatically via IsOpen on right-click
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static T? FindAncestorDataContext<T>(DependencyObject? obj) where T : class
    {
        var current = VisualTreeHelper.GetParent(obj);
        while (current != null)
        {
            if (current is FrameworkElement { DataContext: T found })
                return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
