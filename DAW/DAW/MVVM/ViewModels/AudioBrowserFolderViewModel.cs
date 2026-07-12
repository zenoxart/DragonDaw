using System.Collections.ObjectModel;
using System.IO;
using DAW.MVVM.Models;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// ViewModel for a directory node in the Audio Browser.
///
/// Lazy-loading strategy
/// ──────────────────────
/// 1. Construction: if the folder has sub-items a single
///    <see cref="LoadingPlaceholderViewModel"/> child is added so that
///    the TreeView renders an expand arrow immediately.
/// 2. When <see cref="IsExpanded"/> is first set to <c>true</c> the real
///    children are loaded via <see cref="LoadChildrenAsync"/>, which runs
///    the file-system scan on a background thread (<c>Task.Run</c>) and
///    then replaces the placeholder with real items on the calling (UI)
///    thread.  Subsequent expansions (after collapse) reuse the cached
///    children.
/// 3. <see cref="IsLoading"/> exposes the loading state so the View can
///    show a spinner.
/// </summary>
public sealed class AudioBrowserFolderViewModel : AudioBrowserItemViewModel
{
    private bool _isExpanded;
    private bool _isLoading;
    private bool _childrenLoaded;

    public AudioBrowserFolderViewModel(AudioBrowserFolder model, int depth)
        : base(model.Name, model.FullPath, depth)
    {
        Model    = model;
        Children = [];

        // Placeholder makes the expand arrow visible before the first load
        if (model.HasSubItems)
            Children.Add(LoadingPlaceholderViewModel.Instance);
    }

    public AudioBrowserFolder                          Model    { get; }
    public ObservableCollection<AudioBrowserItemViewModel> Children { get; }

    public override bool IsFolder => true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (value && !_childrenLoaded)
                _ = LoadChildrenAsync();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    // ── Async folder loading ───────────────────────────────────────────────

    private async Task LoadChildrenAsync()
    {
        IsLoading = true;
        try
        {
            System.Diagnostics.Debug.WriteLine($"Loading children for: {FullPath}");
            var items = await Task.Run(() => Scan(FullPath, Depth + 1));
            System.Diagnostics.Debug.WriteLine($"Found {items.Count} items in: {FullPath}");
            
            Children.Clear();
            foreach (var item in items)
                Children.Add(item);
            _childrenLoaded = true;
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Access denied to: {FullPath} - {ex.Message}");
            Children.Clear();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading: {FullPath} - {ex.Message}");
            Children.Clear();
        }
        finally { IsLoading = false; }
    }

    // ── Static scan helper (runs on background thread) ────────────────────

    /// <summary>
    /// Scans <paramref name="path"/> and returns ordered folder + file VMs.
    /// Called on a background thread via <c>Task.Run</c>.
    /// </summary>
    internal static List<AudioBrowserItemViewModel> Scan(string path, int childDepth)
    {
        var result = new List<AudioBrowserItemViewModel>();
        try
        {
            System.Diagnostics.Debug.WriteLine($"Scanning directory: {path}");
            
            // Folders first, alphabetical
            var directories = Directory.EnumerateDirectories(path).OrderBy(Path.GetFileName).ToList();
            System.Diagnostics.Debug.WriteLine($"Found {directories.Count} directories in {path}");
            
            foreach (var dir in directories)
            {
                var name     = Path.GetFileName(dir)!;
                var hasItems = HasSupportedItems(dir);
                var model    = new AudioBrowserFolder(name, dir) { HasSubItems = hasItems };
                result.Add(new AudioBrowserFolderViewModel(model, childDepth));
                System.Diagnostics.Debug.WriteLine($"Added folder: {name} (HasItems: {hasItems})");
            }

            // Audio / MIDI files, alphabetical
            var supportedFiles = Directory.EnumerateFiles(path)
                         .Where(f => AudioBrowserFile.IsSupportedExtension(Path.GetExtension(f)))
                         .OrderBy(Path.GetFileName)
                         .ToList();
            System.Diagnostics.Debug.WriteLine($"Found {supportedFiles.Count} supported files in {path}");
            
            foreach (var file in supportedFiles)
            {
                var name  = Path.GetFileName(file)!;
                var model = new AudioBrowserFile(name, file) { FileSize = new FileInfo(file).Length };
                result.Add(new AudioBrowserFileViewModel(model, childDepth));
                System.Diagnostics.Debug.WriteLine($"Added file: {name}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception scanning {path}: {ex.Message}");
        }
        
        System.Diagnostics.Debug.WriteLine($"Scan complete: {result.Count} total items for {path}");
        return result;
    }

    private static bool HasSupportedItems(string dir)
    {
        try
        {
            // Check for subdirectories first (faster)
            if (Directory.EnumerateDirectories(dir).Any())
                return true;
                
            // Check for supported audio files
            return Directory.EnumerateFiles(dir)
                           .Any(f => AudioBrowserFile.IsSupportedExtension(Path.GetExtension(f)));
        }
        catch { return false; }
    }
}

/// <summary>
/// Sentinel child added to a folder before its real children are loaded.
/// Gives the TreeView a reason to render an expand arrow without touching
/// the file system.
/// </summary>
public sealed class LoadingPlaceholderViewModel : AudioBrowserItemViewModel
{
    public static readonly LoadingPlaceholderViewModel Instance = new();

    private LoadingPlaceholderViewModel() : base("Loading…", string.Empty, 0) { }

    public override bool IsFolder => false;
}
