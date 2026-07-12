using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.MVVM.Models;
using DAW.Services;
using Microsoft.Win32;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// Main ViewModel for the FL Studio-style Audio Browser panel.
///
/// Navigation model
/// ─────────────────
/// Back/Forward history stacks mirror a web-browser navigation model.
/// Each call to <see cref="NavigateToPathAsync"/> pushes the current path
/// onto the back-stack and clears the forward-stack.
///
/// Async loading
/// ──────────────
/// All file-system I/O is dispatched to the thread pool via Task.Run.
/// The UI thread is only touched when writing to ObservableCollections.
///
/// Preview state
/// ──────────────
/// <see cref="AudioPreviewService.StateChanged"/> fires on a background
/// thread.  The handler marshals back to the UI thread through
/// Application.Current.Dispatcher before updating bound properties.
///
/// Drag &amp; Drop / Double-click
/// ──────────────────────────────
/// Double-clicking a file raises <see cref="FileRequestedForPlaylist"/>.
/// The View's code-behind initiates DragDrop for mouse-drag gestures.
/// </summary>
public sealed class AudioBrowserViewModel : INotifyPropertyChanged, IDisposable
{
    // ── Navigation history ────────────────────────────────────────────────
    private readonly Stack<string> _backHistory    = new();
    private readonly Stack<string> _forwardHistory = new();
    private readonly List<string>  _favoritePaths  = [];

    // ── Backing fields ────────────────────────────────────────────────────
    private string _currentPath       = string.Empty;
    private bool   _isLoading;
    private string _searchText        = string.Empty;
    private string _currentPreviewFile = string.Empty;
    private AudioBrowserItemViewModel?  _selectedItem;
    private BrowserCategoryViewModel?   _selectedCategory;
    private AudioBrowserFileViewModel?  _previewingFile;

    // ── Construction ──────────────────────────────────────────────────────
    public AudioBrowserViewModel()
    {
        RootItems  = [];
        Categories = [];

        InitCategories();
        InitCommands();

        AudioPreviewService.Instance.StateChanged += OnPreviewStateChanged;
    }

    // ── Collections ───────────────────────────────────────────────────────
    public ObservableCollection<AudioBrowserItemViewModel> RootItems  { get; }
    public ObservableCollection<BrowserCategoryViewModel>  Categories { get; }

    // ── State properties ──────────────────────────────────────────────────
    public string CurrentPath
    {
        get => _currentPath;
        private set { _currentPath = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            _ = ApplyFilterAsync(value);
        }
    }

    public AudioBrowserItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            _selectedItem = value;
            OnPropertyChanged();
            // Audio preview is only triggered by explicit user action (play button/double-click),
            // not by selection change
        }
    }

    public BrowserCategoryViewModel? SelectedCategory
    {
        get => _selectedCategory;
        private set { _selectedCategory = value; OnPropertyChanged(); }
    }

    public string CurrentPreviewFile
    {
        get => _currentPreviewFile;
        private set
        {
            _currentPreviewFile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreviewFile));
        }
    }

    /// <summary>True while a preview is playing; drives stop-button visibility.</summary>
    public bool HasPreviewFile => !string.IsNullOrEmpty(_currentPreviewFile);

    public bool CanGoBack    => _backHistory.Count > 0;
    public bool CanGoForward => _forwardHistory.Count > 0;

    // ── Commands ──────────────────────────────────────────────────────────
    public ICommand BackCommand        { get; private set; } = null!;
    public ICommand ForwardCommand     { get; private set; } = null!;
    public ICommand AddFolderCommand   { get; private set; } = null!;
    public ICommand RefreshCommand     { get; private set; } = null!;
    public ICommand StopPreviewCommand { get; private set; } = null!;
    public ICommand GoBackCommand      { get; private set; } = null!;
    public ICommand GoForwardCommand   { get; private set; } = null!;
    public ICommand GoToMusicFolderCommand   { get; private set; } = null!;
    public ICommand GoToDesktopCommand       { get; private set; } = null!;
    public ICommand AddToFavoritesCommand    { get; private set; } = null!;
    public ICommand GoToDefaultPathCommand   { get; private set; } = null!;

    // ── Default-path properties ───────────────────────────────────────────

    private string _defaultPath = string.Empty;

    /// <summary>
    /// The user-configured default Audio Browser path (from Settings → Allgemein).
    /// Shown in the nav bar header and used by GoToDefaultPathCommand.
    /// Call <see cref="ReloadDefaultPath"/> after the settings window closes.
    /// </summary>
    public string DefaultPath
    {
        get => _defaultPath;
        private set
        {
            if (_defaultPath == value) return;
            _defaultPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDefaultPath));
            OnPropertyChanged(nameof(DefaultPathLabel));
        }
    }

    /// <summary>True when a non-empty, existing default path is configured.</summary>
    public bool HasDefaultPath => !string.IsNullOrEmpty(_defaultPath) && Directory.Exists(_defaultPath);

    /// <summary>Short display label for the button tooltip (last folder name).</summary>
    public string DefaultPathLabel =>
        HasDefaultPath ? Path.GetFileName(_defaultPath.TrimEnd(Path.DirectorySeparatorChar)) ?? _defaultPath : string.Empty;

    /// <summary>
    /// Re-reads the persisted default path and refreshes bound properties.
    /// Call this after the Settings window closes so the button appears/disappears immediately.
    /// </summary>
    public void ReloadDefaultPath()
    {
        DefaultPath = LoadPersistedAudioBrowserPath();
    }

    /// <summary>List of favorite folder paths.</summary>
    public List<string> FavoritePaths => _favoritePaths;

    // ── Events ────────────────────────────────────────────────────────────
    /// <summary>
    /// Raised when the user double-clicks a file to load it into the Playlist/Arrangement.
    /// The string argument is the full file path.
    /// </summary>
    public event EventHandler<string>? FileRequestedForPlaylist;

    // ── Initialisation ────────────────────────────────────────────────────
    private void InitCategories()
    {
        Categories.Add(new BrowserCategoryViewModel(BrowserCategoryType.Audio,
            "🎵", "Audio – My Music",
            c => _ = NavigateToCategoryAsync(c, GetMusicPath())));

        Categories.Add(new BrowserCategoryViewModel(BrowserCategoryType.Computer,
            "💻", "Computer – All Drives",
            c => _ = LoadDrivesAsync(c)));

        Categories.Add(new BrowserCategoryViewModel(BrowserCategoryType.Documents,
            "📄", "Documents",
            c => _ = NavigateToCategoryAsync(c,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))));

        Categories.Add(new BrowserCategoryViewModel(BrowserCategoryType.Favorites,
            "⭐", "Favorites",
            c => LoadFavorites(c)));

        Categories.Add(new BrowserCategoryViewModel(BrowserCategoryType.Favorites,
            "➕", "Add Folder to Favorites",
            _ => AddFolder()));
    }

    private void InitCommands()
    {
        BackCommand        = new RelayCommand(GoBack,    () => CanGoBack);
        ForwardCommand     = new RelayCommand(GoForward, () => CanGoForward);
        GoBackCommand      = new RelayCommand(GoBack,    () => CanGoBack);
        GoForwardCommand   = new RelayCommand(GoForward, () => CanGoForward);
        AddFolderCommand   = new RelayCommand(AddFolder);
        RefreshCommand     = new RelayCommand(() => _ = RefreshAsync());
        StopPreviewCommand = new RelayCommand(AudioPreviewService.Instance.Stop);
        GoToMusicFolderCommand   = new RelayCommand(async () => await NavigateToPathAsync(GetMusicPath()));
        GoToDesktopCommand       = new RelayCommand(async () => await NavigateToPathAsync(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)));
        AddToFavoritesCommand    = new RelayCommand(AddCurrentPathToFavorites, () => !string.IsNullOrEmpty(_currentPath) && _currentPath != "This PC");
        GoToDefaultPathCommand   = new RelayCommand(
            async () => { if (HasDefaultPath) await NavigateToPathAsync(_defaultPath); },
            () => HasDefaultPath);
    }

    // ── Public API ────────────────────────────────────────────────────────
    public void RequestLoadToPlaylist(string filePath)
        => FileRequestedForPlaylist?.Invoke(this, filePath);

    /// <summary>
    /// Navigates to an arbitrary path, pushing the current path onto the
    /// back-stack so Back() can return to it.
    /// </summary>
    public async Task NavigateToPathAsync(string path)
    {
        if (!Directory.Exists(path)) return;
        if (!string.IsNullOrEmpty(_currentPath))
        {
            _backHistory.Push(_currentPath);
            _forwardHistory.Clear();
        }
        await LoadPathAsync(path);
        RaiseNavigationChanged();
    }

    // ── Navigation ────────────────────────────────────────────────────────
    private void GoBack()
    {
        if (_backHistory.Count == 0) return;
        _forwardHistory.Push(_currentPath);
        _ = LoadPathAsync(_backHistory.Pop());
        RaiseNavigationChanged();
    }

    private void GoForward()
    {
        if (_forwardHistory.Count == 0) return;
        _backHistory.Push(_currentPath);
        _ = LoadPathAsync(_forwardHistory.Pop());
        RaiseNavigationChanged();
    }

    private async Task NavigateToCategoryAsync(BrowserCategoryViewModel cat, string path)
    {
        SetActiveCategory(cat);
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            await NavigateToPathAsync(path);
    }

    // ── Loading ───────────────────────────────────────────────────────────

    private async Task LoadPathAsync(string path)
    {
        IsLoading  = true;
        CurrentPath = path;
        try
        {
            var items = await Task.Run(() => AudioBrowserFolderViewModel.Scan(path, 0));
            RootItems.Clear();
            foreach (var item in items)
                RootItems.Add(item);
        }
        catch { RootItems.Clear(); }
        finally { IsLoading = false; }
    }

    private async Task LoadDrivesAsync(BrowserCategoryViewModel cat)
    {
        SetActiveCategory(cat);
        IsLoading   = true;
        CurrentPath = "This PC";
        try
        {
            var drives = await Task.Run(() =>
                DriveInfo.GetDrives()
                         .Where(d => d.IsReady)
                         .Select(d => (AudioBrowserItemViewModel)new AudioBrowserFolderViewModel(
                             new AudioBrowserFolder(d.Name, d.RootDirectory.FullName) { HasSubItems = true }, 0))
                         .ToList());

            RootItems.Clear();
            foreach (var item in drives)
                RootItems.Add(item);
        }
        finally { IsLoading = false; }
    }

    private async Task RefreshAsync()
    {
        if (_currentPath == "This PC" && SelectedCategory?.CategoryType == BrowserCategoryType.Computer)
        {
            if (SelectedCategory != null)
                await LoadDrivesAsync(SelectedCategory);
        }
        else if (!string.IsNullOrEmpty(_currentPath))
        {
            await LoadPathAsync(_currentPath);
        }
    }

    // ── Search (lazy debounce via rapid re-entry) ─────────────────────────

    private async Task ApplyFilterAsync(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            await RefreshAsync();
            return;
        }

        var root = _currentPath == "This PC" ? null : _currentPath;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        IsLoading = true;
        try
        {
            var results = await Task.Run(() =>
                Directory.EnumerateFiles(root, $"*{filter}*", SearchOption.AllDirectories)
                         .Where(f => AudioBrowserFile.IsSupportedExtension(Path.GetExtension(f)))
                         .Take(200)
                         .Select(f => (AudioBrowserItemViewModel)new AudioBrowserFileViewModel(
                             new AudioBrowserFile(Path.GetFileName(f)!, f) { FileSize = new FileInfo(f).Length },
                             0))
                         .ToList());

            RootItems.Clear();
            foreach (var item in results)
                RootItems.Add(item);
        }
        catch { }
        finally { IsLoading = false; }
    }

    // ── Favorites / Add Folder ────────────────────────────────────────────

    /// <summary>
    /// Initializes the file system browser with default paths.
    /// Respects the user-configured default path from Settings → Allgemein.
    /// </summary>
    public async Task InitializeFileSystemAsync()
    {
        // Read and expose the configured default path (drives the nav button)
        DefaultPath = LoadPersistedAudioBrowserPath();

        // Initialize favorites with common locations
        var musicPath     = GetMusicPath();
        var desktopPath   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var downloadPath  = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        _favoritePaths.Clear();
        if (Directory.Exists(musicPath))     _favoritePaths.Add(musicPath);
        if (Directory.Exists(desktopPath))   _favoritePaths.Add(desktopPath);
        if (Directory.Exists(documentsPath)) _favoritePaths.Add(documentsPath);
        if (Directory.Exists(downloadPath))  _favoritePaths.Add(downloadPath);
        OnPropertyChanged(nameof(FavoritePaths));

        // Navigate: configured path > music folder > desktop
        if (HasDefaultPath)
            await NavigateToPathAsync(_defaultPath);
        else if (Directory.Exists(musicPath))
            await NavigateToPathAsync(musicPath);
        else if (Directory.Exists(desktopPath))
            await NavigateToPathAsync(desktopPath);
    }

    /// <summary>
    /// Reads the AudioBrowserDefaultPath from the persisted ui_settings.json.
    /// Returns empty string if not set.
    /// </summary>
    private static string LoadPersistedAudioBrowserPath()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Lapis DAW", "ui_settings.json");
            if (!File.Exists(path)) return string.Empty;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("AudioBrowserDefaultPath", out var prop))
                return prop.GetString() ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private static string GetMusicPath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    }

    private void AddCurrentPathToFavorites()
    {
        if (string.IsNullOrEmpty(_currentPath) || _currentPath == "This PC") return;
        if (!_favoritePaths.Contains(_currentPath, StringComparer.OrdinalIgnoreCase))
        {
            _favoritePaths.Add(_currentPath);
            OnPropertyChanged(nameof(FavoritePaths));
        }
    }

    public void RemoveFromFavorites(string path)
    {
        _favoritePaths.Remove(path);
        OnPropertyChanged(nameof(FavoritePaths));
    }

    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Add Folder to Browser Favorites" };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FolderName;
        if (!_favoritePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            _favoritePaths.Add(path);

        _ = NavigateToPathAsync(path);
    }

    private void LoadFavorites(BrowserCategoryViewModel cat)
    {
        SetActiveCategory(cat);
        CurrentPath = "Favorites";
        RootItems.Clear();

        foreach (var path in _favoritePaths.Where(Directory.Exists))
        {
            var name   = Path.GetFileName(path) ?? path;
            var folder = new AudioBrowserFolder(name, path) { HasSubItems = true };
            RootItems.Add(new AudioBrowserFolderViewModel(folder, 0));
        }
    }

    // ── Preview state ─────────────────────────────────────────────────────

    private void OnPreviewStateChanged(object? sender, PreviewStateEventArgs e)
    {
        // Marshal to UI thread — StateChanged fires on a background thread
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            // Clear previous previewing flag
            if (_previewingFile != null)
            {
                _previewingFile.IsPreviewing = false;
                _previewingFile = null;
            }

            if (e.IsPlaying && e.FilePath != null)
            {
                CurrentPreviewFile = $"▶  {Path.GetFileName(e.FilePath)}";

                // Find the matching file VM in the current root items and flag it
                _previewingFile = FindFileVm(RootItems, e.FilePath);
                if (_previewingFile != null)
                    _previewingFile.IsPreviewing = true;
            }
            else
            {
                CurrentPreviewFile = string.Empty;
            }
        });
    }

    private static AudioBrowserFileViewModel? FindFileVm(
        IEnumerable<AudioBrowserItemViewModel> items, string path)
    {
        foreach (var item in items)
        {
            if (item is AudioBrowserFileViewModel fvm &&
                string.Equals(fvm.FullPath, path, StringComparison.OrdinalIgnoreCase))
                return fvm;

            if (item is AudioBrowserFolderViewModel folderVm)
            {
                var found = FindFileVm(folderVm.Children, path);
                if (found != null) return found;
            }
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetActiveCategory(BrowserCategoryViewModel cat)
    {
        foreach (var c in Categories) c.IsSelected = false;
        cat.IsSelected    = true;
        SelectedCategory  = cat;
    }

    private void RaiseNavigationChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
        => AudioPreviewService.Instance.StateChanged -= OnPreviewStateChanged;
}
