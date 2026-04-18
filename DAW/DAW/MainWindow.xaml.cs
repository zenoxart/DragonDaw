using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DAW.Models;
using DAW.Plugins;
using DAW.ViewModels;
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
            }
        }
        
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (_viewModel.EditMenuViewModel.DeleteCommand.CanExecute(null))
                _viewModel.EditMenuViewModel.DeleteCommand.Execute(null);
            e.Handled = true;
            return;
        }
        
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
            SamplerWindow.ShowForTrack(track, this);
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
}