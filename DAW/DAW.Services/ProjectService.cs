using System.IO;
using DAW.MVVM.Models;
using Microsoft.Win32;

namespace DAW.Services;

/// <summary>
/// Implementation of project service with full MVVM support
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ISettingsService _settingsService;
    private bool _isProcessing;
    private string? _currentProjectPath;
    private bool _isDirty;

    public ProjectService(IFileSystemService fileSystemService, ISettingsService settingsService)
    {
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public bool HasCurrentProject => !string.IsNullOrEmpty(_currentProjectPath);
    public bool HasBackup => HasCurrentProject && File.Exists(GetBackupPath());
    public bool IsProcessing 
    { 
        get => _isProcessing;
        private set => _isProcessing = value;
    }
    
    public string? CurrentProjectPath => _currentProjectPath;

    public event EventHandler<bool>? ProjectDirtyStateChanged;
    public event EventHandler? RecentProjectsChanged;

    public async Task CreateNewProjectAsync()
    {
        IsProcessing = true;
        try
        {
            // Reset current state
            _currentProjectPath = null;
            SetDirtyState(false);
            
            // Initialize new project
            await InitializeNewProject();
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task CreateFromTemplateAsync(ProjectTemplate template)
    {
        IsProcessing = true;
        try
        {
            var templateContent = await _fileSystemService.ReadAllTextAsync(template.FilePath);
            
            // Initialize new project with template
            await InitializeNewProject(templateContent);
            SetDirtyState(true); // Mark as dirty since it's based on template
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task OpenProjectAsync()
    {
        IsProcessing = true;
        try
        {
            var filePath = await _fileSystemService.ShowOpenFileDialogAsync(
                "DAW Project Files (*.dawproj)|*.dawproj|All Files (*.*)|*.*",
                "Projekt öffnen"
            );

            if (!string.IsNullOrEmpty(filePath))
            {
                await LoadProjectAsync(filePath);
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task OpenRecentProjectAsync(RecentProject recentProject)
    {
        IsProcessing = true;
        try
        {
            if (await _fileSystemService.FileExistsAsync(recentProject.FilePath))
            {
                await LoadProjectAsync(recentProject.FilePath);
            }
            else
            {
                // Remove from recent projects if file doesn't exist
                var recentProjects = (await _settingsService.GetRecentProjectsAsync()).ToList();
                recentProjects.RemoveAll(p => p.FilePath == recentProject.FilePath);
                await _settingsService.SetRecentProjectsAsync(recentProjects);
                RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task SaveProjectAsync()
    {
        if (!HasCurrentProject)
        {
            await SaveAsProjectAsync();
            return;
        }

        IsProcessing = true;
        try
        {
            await CreateBackupAsync();
            await SaveProjectToPathAsync(_currentProjectPath!);
            SetDirtyState(false);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task SaveAsProjectAsync()
    {
        IsProcessing = true;
        try
        {
            var filePath = await _fileSystemService.ShowSaveFileDialogAsync(
                "DAW Project Files (*.dawproj)|*.dawproj",
                "Projekt speichern unter",
                GetSuggestedFileName()
            );

            if (!string.IsNullOrEmpty(filePath))
            {
                await SaveProjectToPathAsync(filePath);
                _currentProjectPath = filePath;
                AddRecentProject(filePath);
                SetDirtyState(false);
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task SaveNewVersionAsync()
    {
        IsProcessing = true;
        try
        {
            var versionedPath = GenerateVersionedPath(_currentProjectPath ?? "Untitled");
            await SaveProjectToPathAsync(versionedPath);
            _currentProjectPath = versionedPath;
            AddRecentProject(versionedPath);
            SetDirtyState(false);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task SaveAsTemplateAsync()
    {
        IsProcessing = true;
        try
        {
            var templateName = await PromptForTemplateNameAsync();
            if (!string.IsNullOrEmpty(templateName))
            {
                var templatesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DAW Templates");
                Directory.CreateDirectory(templatesDir);
                
                var templatePath = Path.Combine(templatesDir, $"{templateName}.dawproj");
                await SaveProjectToPathAsync(templatePath);

                // Add to templates list
                var templates = (await _settingsService.GetTemplatesAsync()).ToList();
                templates.Add(new ProjectTemplate
                {
                    Name = templateName,
                    FilePath = templatePath,
                    Category = "User",
                    Description = $"Benutzerdefiniertes Template erstellt am {DateTime.Now:dd.MM.yyyy}"
                });
                await _settingsService.SetTemplatesAsync(templates);
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task ImportAsync(string format)
    {
        IsProcessing = true;
        try
        {
            var filter = format.ToLower() switch
            {
                "midi" => "MIDI Files (*.mid;*.midi)|*.mid;*.midi",
                "audio" => "Audio Files (*.wav;*.mp3;*.ogg;*.flac)|*.wav;*.mp3;*.ogg;*.flac",
                "flp" => "FL Studio Projects (*.flp)|*.flp",
                "als" => "Ableton Live Sets (*.als)|*.als",
                _ => "All Files (*.*)|*.*"
            };

            var filePath = await _fileSystemService.ShowOpenFileDialogAsync(filter, $"{format.ToUpper()} importieren");
            
            if (!string.IsNullOrEmpty(filePath))
            {
                await ProcessImportAsync(filePath, format);
                SetDirtyState(true);
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task ExportAsync(string format)
    {
        IsProcessing = true;
        try
        {
            var filter = format.ToLower() switch
            {
                "midi" => "MIDI Files (*.mid)|*.mid",
                "audio" => "Audio Files (*.wav)|*.wav",
                "stems" => "Audio Files (*.wav)|*.wav",
                _ => "All Files (*.*)|*.*"
            };

            var fileName = format.ToLower() == "stems" 
                ? $"{_fileSystemService.GetFileNameWithoutExtension(_currentProjectPath ?? "Export")}_Stems"
                : _fileSystemService.GetFileNameWithoutExtension(_currentProjectPath ?? "Export");

            var filePath = await _fileSystemService.ShowSaveFileDialogAsync(filter, $"Als {format.ToUpper()} exportieren", fileName);
            
            if (!string.IsNullOrEmpty(filePath))
            {
                await ProcessExportAsync(filePath, format);
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task RevertToBackupAsync()
    {
        if (!HasBackup) return;

        IsProcessing = true;
        try
        {
            var backupPath = GetBackupPath();
            var backupContent = await _fileSystemService.ReadAllTextAsync(backupPath);
            await LoadProjectFromContentAsync(backupContent);
            SetDirtyState(false);
        }
        finally
        {
            IsProcessing = false;
        }
    }

    public async Task<bool> PromptSaveChangesAsync()
    {
        if (!_isDirty) return true;

        var result = System.Windows.MessageBox.Show(
            "Das aktuelle Projekt enthält ungespeicherte Änderungen.\n\nMöchten Sie diese speichern?",
            "Ungespeicherte Änderungen",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question
        );

        return result switch
        {
            System.Windows.MessageBoxResult.Yes => await TrySaveAsync(),
            System.Windows.MessageBoxResult.No => true,
            _ => false
        };
    }

    public async Task<IEnumerable<ProjectTemplate>> GetAvailableTemplatesAsync()
    {
        return await _settingsService.GetTemplatesAsync();
    }

    public async Task<IEnumerable<RecentProject>> GetRecentProjectsAsync()
    {
        var recentProjects = await _settingsService.GetRecentProjectsAsync();
        return recentProjects.OrderByDescending(p => p.LastOpened);
    }

    public void AddRecentProject(string filePath)
    {
        Task.Run(async () =>
        {
            var recentProjects = (await _settingsService.GetRecentProjectsAsync()).ToList();
            
            // Remove if already exists
            recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            
            // Add to beginning
            recentProjects.Insert(0, new RecentProject
            {
                FilePath = filePath,
                DisplayName = _fileSystemService.GetFileNameWithoutExtension(filePath),
                LastOpened = DateTime.Now,
                Exists = await _fileSystemService.FileExistsAsync(filePath)
            });

            // Keep only last 10
            if (recentProjects.Count > 10)
            {
                recentProjects = recentProjects.Take(10).ToList();
            }

            await _settingsService.SetRecentProjectsAsync(recentProjects);
            RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void ClearRecentProjects()
    {
        Task.Run(async () =>
        {
            await _settingsService.SetRecentProjectsAsync(new List<RecentProject>());
            RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void SetDirtyState(bool isDirty)
    {
        if (_isDirty != isDirty)
        {
            _isDirty = isDirty;
            ProjectDirtyStateChanged?.Invoke(this, isDirty);
        }
    }

    private async Task InitializeNewProject(string? templateContent = null)
    {
        // Initialize project with template content or empty state
        var content = templateContent ?? GenerateEmptyProjectContent();
        await LoadProjectFromContentAsync(content);
    }

    private async Task LoadProjectAsync(string filePath)
    {
        var content = await _fileSystemService.ReadAllTextAsync(filePath);
        await LoadProjectFromContentAsync(content);
        _currentProjectPath = filePath;
        AddRecentProject(filePath);
        SetDirtyState(false);
    }

    private async Task LoadProjectFromContentAsync(string content)
    {
        // Parse and load project content
        // This would be specific to your project format
        await Task.CompletedTask; // Placeholder
    }

    private async Task SaveProjectToPathAsync(string filePath)
    {
        var content = GenerateProjectContent();
        await _fileSystemService.WriteAllTextAsync(filePath, content);
    }

    private async Task CreateBackupAsync()
    {
        if (!HasCurrentProject) return;
        
        var backupPath = GetBackupPath();
        await _fileSystemService.CopyFileAsync(_currentProjectPath!, backupPath);
    }

    private string GetBackupPath()
    {
        return _currentProjectPath + ".backup";
    }

    private string GenerateEmptyProjectContent()
    {
        // Generate empty project structure
        return "{}"; // Placeholder
    }

    private string GenerateProjectContent()
    {
        // Serialize current project state
        return "{}"; // Placeholder
    }

    private string GetSuggestedFileName()
    {
        return HasCurrentProject 
            ? _fileSystemService.GetFileNameWithoutExtension(_currentProjectPath!)
            : "Neues Projekt";
    }

    private string GenerateVersionedPath(string originalPath)
    {
        var directory = _fileSystemService.GetDirectoryName(originalPath);
        var fileNameWithoutExt = _fileSystemService.GetFileNameWithoutExtension(originalPath);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return Path.Combine(directory, $"{fileNameWithoutExt}_v{timestamp}.dawproj");
    }

    private async Task<string?> PromptForTemplateNameAsync()
    {
        // In a real implementation, you'd show a dialog
        return await Task.FromResult("Neues Template");
    }

    private async Task ProcessImportAsync(string filePath, string format)
    {
        // Process import based on format
        await Task.CompletedTask; // Placeholder
    }

    private async Task ProcessExportAsync(string filePath, string format)
    {
        // Process export based on format
        await Task.CompletedTask; // Placeholder
    }

    private async Task<bool> TrySaveAsync()
    {
        try
        {
            await SaveProjectAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}