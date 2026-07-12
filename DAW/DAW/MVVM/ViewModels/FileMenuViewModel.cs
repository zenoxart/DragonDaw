using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DAW.Commands;
using DAW.MVVM.Models;
using DAW.Services;

namespace DAW.MVVM.ViewModels;

/// <summary>
/// Enhanced ViewModel for the File menu with full project management support
/// </summary>
public class FileMenuViewModel : INotifyPropertyChanged
{
    private readonly IProjectService _projectService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private bool _hasUnsavedChanges;
    
    public FileMenuViewModel(
        IProjectService projectService, 
        IFileSystemService fileSystemService,
        ISettingsService settingsService,
        MainViewModel mainViewModel)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));

        InitializeCommands();
        
        // Build menu asynchronously
        Task.Run(async () => await BuildFileMenuAsync());
        
        // Subscribe to events
        _projectService.ProjectDirtyStateChanged += OnProjectDirtyStateChanged;
        _projectService.RecentProjectsChanged += OnRecentProjectsChanged;
    }

    public ObservableCollection<MenuItemViewModel> FileMenuItems { get; } = new();

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => SetProperty(ref _hasUnsavedChanges, value);
    }

    #region Commands
    
    public ICommand NewProjectCommand { get; private set; } = null!;
    public ICommand OpenProjectCommand { get; private set; } = null!;
    public ICommand SaveProjectCommand { get; private set; } = null!;
    public ICommand SaveProjectAsCommand { get; private set; } = null!;
    public ICommand SaveAsProjectCommand => SaveProjectAsCommand; // Alias für Kompatibilität
    public ICommand OpenRecentCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;
    public ICommand ExportCommand { get; private set; } = null!;
    
    #endregion

    private void InitializeCommands()
    {
        NewProjectCommand = new AsyncRelayCommand(ExecuteNewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(ExecuteOpenProjectAsync);
        SaveProjectCommand = new AsyncRelayCommand(ExecuteSaveProjectAsync);
        SaveProjectAsCommand = new AsyncRelayCommand(ExecuteSaveAsProjectAsync);
        OpenRecentCommand = new AsyncRelayCommand<RecentProject>(ExecuteOpenRecentAsync);
        ExitCommand = new AsyncRelayCommand(ExecuteExitAsync);
        ExportCommand = _mainViewModel.ExportCommand;
    }

    private async Task ExecuteNewProjectAsync()
    {
        if (_mainViewModel.NewProjectCommand is AsyncRelayCommand asyncCmd)
            await asyncCmd.ExecuteAsync();
        else
            _mainViewModel.NewProjectCommand.Execute(null);
    }

    private async Task ExecuteOpenProjectAsync()
    {
        if (_mainViewModel.OpenProjectCommand is AsyncRelayCommand asyncCmd)
            await asyncCmd.ExecuteAsync();
        else
            _mainViewModel.OpenProjectCommand.Execute(null);
    }

    private async Task ExecuteSaveProjectAsync()
    {
        if (_mainViewModel.SaveProjectCommand is AsyncRelayCommand asyncCmd)
            await asyncCmd.ExecuteAsync();
        else
            _mainViewModel.SaveProjectCommand.Execute(null);
    }

    private async Task ExecuteSaveAsProjectAsync()
    {
        if (_mainViewModel.SaveProjectAsCommand is AsyncRelayCommand asyncCmd)
            await asyncCmd.ExecuteAsync();
        else
            _mainViewModel.SaveProjectAsCommand.Execute(null);
    }

    private async Task ExecuteExitAsync()
    {
        // Check for unsaved changes
        if (_mainViewModel.EnhancedProjectService.HasUnsavedChanges)
        {
            // In a real app, show a dialog asking to save
            await ExecuteSaveProjectAsync();
        }
        
        System.Windows.Application.Current.Shutdown();
    }

    private async Task ExecuteOpenRecentAsync(RecentProject? recentProject)
    {
        if (recentProject?.FilePath != null && System.IO.File.Exists(recentProject.FilePath))
        {
            try
            {
                var project = await _mainViewModel.EnhancedProjectService.OpenProjectAsync(recentProject.FilePath);
                await _mainViewModel.EnhancedProjectService.ImportProjectState(project, _mainViewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open recent project: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Gets recent projects for compatibility with KeyboardShortcutManager
    /// </summary>
    public async Task<IEnumerable<RecentProject>> GetRecentProjectsAsync()
    {
        try
        {
            return await _settingsService.GetRecentProjectsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get recent projects: {ex.Message}");
            return Enumerable.Empty<RecentProject>();
        }
    }

    private async Task BuildFileMenuAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            FileMenuItems.Clear();

            // New Project
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Neues Projekt",
                Command = NewProjectCommand,
                Icon = "🆕",
                InputGestureText = "Ctrl+N"
            }));

            // Open Project
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Öffnen...",
                Command = OpenProjectCommand,
                Icon = "📂",
                InputGestureText = "Ctrl+O"
            }));

            // Separator
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel { IsSeparator = true }));

            // Save Project
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Speichern",
                Command = SaveProjectCommand,
                Icon = "💾",
                InputGestureText = "Ctrl+S"
            }));

            // Save As
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Speichern unter...",
                Command = SaveProjectAsCommand,
                Icon = "💾",
                InputGestureText = "Ctrl+Shift+S"
            }));

            // Separator
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel { IsSeparator = true }));

            // Export
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Exportieren...",
                Command = ExportCommand,
                Icon = "🎵",
                InputGestureText = "Ctrl+Shift+E"
            }));

            // Separator
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel { IsSeparator = true }));

            // Exit
            FileMenuItems.Add(new MenuItemViewModel(new MenuItemModel
            {
                Header = "Beenden",
                Command = ExitCommand,
                Icon = "🚪",
                InputGestureText = "Alt+F4"
            }));
        });
    }

    private void OnProjectDirtyStateChanged(object? sender, bool hasUnsavedChanges)
    {
        HasUnsavedChanges = hasUnsavedChanges;
    }

    private void OnRecentProjectsChanged(object? sender, EventArgs e)
    {
        Task.Run(async () => await BuildFileMenuAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}