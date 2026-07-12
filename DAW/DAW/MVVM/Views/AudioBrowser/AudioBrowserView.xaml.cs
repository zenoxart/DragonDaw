using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DAW.MVVM.ViewModels;

namespace DAW.MVVM.Views.AudioBrowser;

/// <summary>
/// Code-behind for AudioBrowserView.
///
/// Responsibilities
/// ─────────────────
/// • Forwarding TreeView selection changes to the ViewModel.
/// • Initiating drag-and-drop when the user drags an audio file row.
/// • Translating double-click on a file to the LoadToPlaylist action.
/// • Handling favorites click events.
///
/// Everything else (navigation, async loading, preview) is handled
/// entirely in <see cref="AudioBrowserViewModel"/>.
/// </summary>
public partial class AudioBrowserView : UserControl
{
    private Point _dragStartPoint;

    public AudioBrowserView()
    {
        InitializeComponent();
        
        // Initialize the browser with file system when loaded
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioBrowserViewModel vm)
        {
            await vm.InitializeFileSystemAsync();
        }
    }

    // ── TreeView event handling ──────────────────────────────────────────

    private void BrowserTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is AudioBrowserViewModel vm)
            vm.SelectedItem = e.NewValue as AudioBrowserItemViewModel;
    }

    private void BrowserTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && 
            item.DataContext is AudioBrowserFolderViewModel folder)
        {
            folder.IsExpanded = true;
        }
    }

    private void BrowserTree_Collapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && 
            item.DataContext is AudioBrowserFolderViewModel folder)
        {
            folder.IsExpanded = false;
        }
    }

    // ── Favorites handling ────────────────────────────────────────────────

    private async void Favorite_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is string path)
        {
            if (DataContext is AudioBrowserViewModel vm)
            {
                await vm.NavigateToPathAsync(path);
            }
        }
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is string path)
        {
            if (DataContext is AudioBrowserViewModel vm)
            {
                vm.RemoveFromFavorites(path);
            }
        }
    }

    // ── Drag & Drop ────────────────────────────────────────────────────────

    private void BrowserTree_PreviewMouseLeftButtonDown(
        object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    /// <summary>
    /// Starts a DragDrop operation once the mouse has moved beyond the
    /// system drag threshold.  The DataObject carries both
    /// <c>DataFormats.FileDrop</c> (for the Arrangement drop handler) and
    /// a custom "AudioFilePath" format for internal use.
    /// </summary>
    private void BrowserTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var delta = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        System.Diagnostics.Debug.WriteLine("Mouse move detected, checking for drag start");

        // Walk the visual tree to find the TreeViewItem under the cursor
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element is not TreeViewItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is not FrameworkElement fe || fe.DataContext is not AudioBrowserFileViewModel fileVm || !fileVm.IsAudio)
        {
            System.Diagnostics.Debug.WriteLine("No valid audio file found under cursor");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"Starting drag operation for: {fileVm.FullPath}");

        var data = new DataObject(DataFormats.FileDrop, new[] { fileVm.FullPath });
        data.SetData("AudioFilePath", fileVm.FullPath);
        data.SetData(typeof(AudioBrowserFileViewModel), fileVm); // Add the ViewModel object

        // Provide visual feedback during drag
        try
        {
            System.Diagnostics.Debug.WriteLine("Calling DragDrop.DoDragDrop");
            DragDrop.DoDragDrop(
                (DependencyObject)e.Source,
                data,
                DragDropEffects.Copy | DragDropEffects.Link);
            System.Diagnostics.Debug.WriteLine("Drag operation completed");
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drag operation cancelled or failed: {ex.Message}");
        }
    }

    // ── Double-click → load into Playlist ─────────────────────────────────

    private void BrowserTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not AudioBrowserViewModel vm) return;
        if (vm.SelectedItem is not AudioBrowserFileViewModel fileVm) return;

        vm.RequestLoadToPlaylist(fileVm.FullPath);
        e.Handled = true;
    }

    // ── Default-path label click → navigate to default path ──────────────────

    private async void DefaultPathLabel_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AudioBrowserViewModel vm && vm.HasDefaultPath)
            await vm.NavigateToPathAsync(vm.DefaultPath);
        e.Handled = true;
    }
}
