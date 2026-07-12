using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using DAW.MVVM.Models;
using DAW.MVVM.ViewModels;
using DAW.Plugins;
using DAW.Views;

namespace DAW;

/// <summary>
/// Lapis DAW main window with drag and drop support and plugin system.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    
    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
    }

    // ── Transport-bar BPM box ────────────────────────────────────────────

    /// <summary>Select all on focus so typing replaces the old value.</summary>
    private void TransportBpmBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.Dispatcher.InvokeAsync(tb.SelectAll, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Enter commits the typed BPM (binding updates on demand), Escape reverts
    /// to the current value. The binding itself is LostFocus — never per
    /// keystroke, so the ViewModel's Math.Clamp can no longer mangle partial
    /// input while typing (the old "96 becomes 206" bug).
    /// </summary>
    private void TransportBpmBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        if (e.Key == Key.Enter)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();  // commit
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();  // show clamped value
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();  // revert text
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }
    
    /// <summary>
    /// Global Space = Play/Stop toggle.
    ///
    /// Handled in PREVIEW (tunneling) so it always works: with the bubbling
    /// KeyDown, Space never reached the window when focus sat on a Button
    /// (Space "clicks" the focused button — e.g. right after clicking Play!)
    /// or inside a ScrollViewer (Space scrolls). Text input stays untouched:
    /// when a TextBox has focus, Space types a space as usual.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None) return;
        if (Keyboard.FocusedElement is TextBox) return;   // typing has priority

        if (_viewModel.IsPlaying)
            _viewModel.StopAllCommand.Execute(null);       // second press → Stop
        else
            _viewModel.PlayAllCommand.Execute(null);       // first press → Play

        e.Handled = true;
    }

    /// <summary>
    /// Handles keyboard shortcuts.
    /// </summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // First, try to handle file menu shortcuts (if available)
        if (_viewModel.KeyboardShortcutManager?.HandleKeyDown(e) == true)
        {
            return; // Shortcut was handled
        }
        
        // Edit shortcuts (Ctrl+Z/Y/X/C/V, Delete)
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    if (_viewModel.EditMenuViewModel.UndoCommand.CanExecute(null))
                        _viewModel.EditMenuViewModel.UndoCommand.Execute(null);
                    e.Handled = true; return;
                case Key.Y:
                    if (_viewModel.EditMenuViewModel.RedoCommand.CanExecute(null))
                        _viewModel.EditMenuViewModel.RedoCommand.Execute(null);
                    e.Handled = true; return;
                case Key.X:
                    if (_viewModel.EditMenuViewModel.CutCommand.CanExecute(null))
                        _viewModel.EditMenuViewModel.CutCommand.Execute(null);
                    e.Handled = true; return;
                case Key.C:
                    if (_viewModel.EditMenuViewModel.CopyCommand.CanExecute(null))
                        _viewModel.EditMenuViewModel.CopyCommand.Execute(null);
                    e.Handled = true; return;
                case Key.V:
                    if (_viewModel.EditMenuViewModel.PasteCommand.CanExecute(null))
                        _viewModel.EditMenuViewModel.PasteCommand.Execute(null);
                    e.Handled = true; return;
                case Key.A:
                    if (_viewModel.EditMenuViewModel.SelectAllCommand.CanExecute(null))
                        _viewModel.EditMenuViewModel.SelectAllCommand.Execute(null);
                    e.Handled = true; return;
                case Key.B:
                    _viewModel.IsAudioBrowserVisible = !_viewModel.IsAudioBrowserVisible;
                    e.Handled = true; return;
            }
        }
        
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_viewModel.EditMenuViewModel.DeleteCommand.CanExecute(null))
                _viewModel.EditMenuViewModel.DeleteCommand.Execute(null);
            e.Handled = true;
            return;
        }
        
        // (Space play/stop toggle lives in Window_PreviewKeyDown — tunneling —
        //  so focused buttons and scroll viewers can never swallow it.)
        
        // Ctrl+P opens plugin palette
        if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenPluginPalette();
            e.Handled = true;
        }
        // Ctrl+Shift+P opens plugin palette for selected track
        else if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenPluginPalette(_viewModel.SelectedTrack);
            e.Handled = true;
        }
        // Ctrl+Shift+E opens export window
        else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.ExportCommand.CanExecute(null))
                _viewModel.ExportCommand.Execute(null);
            e.Handled = true;
        }
        // Ctrl+E opens sampler for selected track
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.OpenSampler();
            e.Handled = true;
        }
    }
    
    /// <summary>
    /// Handles double-click on track to open Sampler/Clip Editor.
    /// </summary>
    private void Track_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: Track track })
        {
            MVVM.Views.SamplerWindow.ShowForTrack(track, this);
            e.Handled = true;
        }
    }
    
    /// <summary>
    /// Opens the plugin command palette.
    /// </summary>
    private void OpenPluginPalette(Track? targetTrack = null)
    {
        CommandPalette.Show(this, targetTrack);
    }
    
    /// <summary>
    /// Button click handler for plugin palette.
    /// </summary>
    private void OpenPluginPalette_Click(object sender, RoutedEventArgs e)
    {
        OpenPluginPalette(_viewModel.SelectedTrack);
    }
    
    /// <summary>
    /// Handles drag over events for visual feedback.
    /// </summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            bool hasAudioFiles = files.Any(f => IsAudioFile(f));
            
            e.Effects = hasAudioFiles ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        
        e.Handled = true;
    }
    
    /// <summary>
    /// Handles drop events on the window.
    /// </summary>
    private void Window_Drop(object sender, DragEventArgs e)
    {
        HandleFileDrop(e);
    }
    
    /// <summary>
    /// Handles drop events specifically on the playlist area.
    /// </summary>
    private void Playlist_Drop(object sender, DragEventArgs e)
    {
        HandleFileDrop(e);
    }
    
    /// <summary>
    /// Opens the Options window.
    /// </summary>
    private void Options_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenOptionsCommand.Execute(null);
    }
    
    /// <summary>
    /// Common handler for file drops.
    /// </summary>
    private void HandleFileDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var audioFiles = files.Where(IsAudioFile).ToArray();
        
        if (audioFiles.Length > 0)
        {
            _viewModel.AddFilesAsTrack(audioFiles);
        }
        
        e.Handled = true;
    }
    
    /// <summary>
    /// Checks if a file is a supported audio format.
    /// </summary>
    private static bool IsAudioFile(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".mp3" or ".wav" or ".wma" or ".m4a" or ".flac" or ".ogg";
    }

    // ── Borderless Window Controls ────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            // Allow drag-move; if maximized, restore first for smooth drag
            if (WindowState == WindowState.Maximized)
            {
                var point = PointToScreen(e.GetPosition(this));
                WindowState = WindowState.Normal;
                Left = point.X - (ActualWidth / 2);
                Top = point.Y - 15;
            }
            DragMove();
        }
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            // Prevent covering the taskbar
            WindowBorder.Margin = new Thickness(7);
            MaxRestoreBtn.Content = "\uE923"; // Restore icon
            MaxRestoreBtn.ToolTip = "Wiederherstellen";
        }
        else
        {
            WindowBorder.Margin = new Thickness(0);
            MaxRestoreBtn.Content = "\uE922"; // Maximize icon
            MaxRestoreBtn.ToolTip = "Maximieren";
        }
    }

    /// <summary>
    /// Enable native window resizing for borderless window via WM_NCHITTEST.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
        HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int ResizeGrip = 6;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
        {
            var screenPoint = new System.Windows.Point(
                (short)(lParam.ToInt32() & 0xFFFF),
                (short)(lParam.ToInt32() >> 16));
            var clientPoint = PointFromScreen(screenPoint);

            double w = ActualWidth, h = ActualHeight;
            bool left = clientPoint.X < ResizeGrip;
            bool right = clientPoint.X > w - ResizeGrip;
            bool top = clientPoint.Y < ResizeGrip;
            bool bottom = clientPoint.Y > h - ResizeGrip;

            if (top && left) { handled = true; return HTTOPLEFT; }
            if (top && right) { handled = true; return HTTOPRIGHT; }
            if (bottom && left) { handled = true; return HTBOTTOMLEFT; }
            if (bottom && right) { handled = true; return HTBOTTOMRIGHT; }
            if (left) { handled = true; return HTLEFT; }
            if (right) { handled = true; return HTRIGHT; }
            if (top) { handled = true; return HTTOP; }
            if (bottom) { handled = true; return HTBOTTOM; }
        }
        return IntPtr.Zero;
    }
}