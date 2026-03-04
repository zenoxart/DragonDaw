using DAW.Models;

namespace DAW.Services;

/// <summary>
/// Service interface for project-related operations
/// </summary>
public interface IProjectService
{
    bool HasCurrentProject { get; }
    bool HasBackup { get; }
    bool IsProcessing { get; }
    string? CurrentProjectPath { get; }

    event EventHandler<bool>? ProjectDirtyStateChanged;
    event EventHandler? RecentProjectsChanged;

    Task CreateNewProjectAsync();
    Task CreateFromTemplateAsync(ProjectTemplate template);
    Task OpenProjectAsync();
    Task OpenRecentProjectAsync(RecentProject recentProject);
    Task SaveProjectAsync();
    Task SaveAsProjectAsync();
    Task SaveNewVersionAsync();
    Task SaveAsTemplateAsync();
    Task ImportAsync(string format);
    Task ExportAsync(string format);
    Task RevertToBackupAsync();
    Task<bool> PromptSaveChangesAsync();

    Task<IEnumerable<ProjectTemplate>> GetAvailableTemplatesAsync();
    Task<IEnumerable<RecentProject>> GetRecentProjectsAsync();
    void AddRecentProject(string filePath);
    void ClearRecentProjects();
}

/// <summary>
/// Service interface for file system operations
/// </summary>
public interface IFileSystemService
{
    Task<string?> ShowOpenFileDialogAsync(string filter, string title = "Datei öffnen");
    Task<string?> ShowSaveFileDialogAsync(string filter, string title = "Datei speichern", string? defaultFileName = null);
    Task<bool> FileExistsAsync(string path);
    Task<bool> DirectoryExistsAsync(string path);
    Task<string> ReadAllTextAsync(string path);
    Task WriteAllTextAsync(string path, string content);
    Task CopyFileAsync(string sourcePath, string destinationPath);
    Task<string> GetBackupPathAsync(string originalPath);
    string GetFileNameWithoutExtension(string path);
    string GetDirectoryName(string path);
}

/// <summary>
/// Service interface for application settings
/// </summary>
public interface ISettingsService
{
    Task<T> GetSettingAsync<T>(string key, T defaultValue);
    Task SetSettingAsync<T>(string key, T value);
    Task<IList<RecentProject>> GetRecentProjectsAsync();
    Task SetRecentProjectsAsync(IList<RecentProject> recentProjects);
    Task<IList<ProjectTemplate>> GetTemplatesAsync();
    Task SetTemplatesAsync(IList<ProjectTemplate> templates);
}