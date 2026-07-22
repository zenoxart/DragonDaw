using System.IO;
using Microsoft.Win32;

namespace DAW.Services;

/// <summary>
/// File system service implementation
/// </summary>
public class FileSystemService : IFileSystemService
{
    public async Task<string?> ShowOpenFileDialogAsync(string filter, string title = "Datei öffnen")
    {
        return await Task.Run(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                CheckFileExists = true,
                CheckPathExists = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });
    }

    public async Task<string?> ShowSaveFileDialogAsync(string filter, string title = "Datei speichern", string? defaultFileName = null)
    {
        return await Task.Run(() =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName ?? string.Empty,
                OverwritePrompt = true
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });
    }

    public async Task<bool> FileExistsAsync(string path)
    {
        return await Task.FromResult(File.Exists(path));
    }

    public async Task<bool> DirectoryExistsAsync(string path)
    {
        return await Task.FromResult(Directory.Exists(path));
    }

    public async Task<string> ReadAllTextAsync(string path)
    {
        return await File.ReadAllTextAsync(path);
    }

    public async Task WriteAllTextAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content);
    }

    public async Task CopyFileAsync(string sourcePath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await Task.Run(() => File.Copy(sourcePath, destinationPath, true));
    }

    public async Task<string> GetBackupPathAsync(string originalPath)
    {
        return await Task.FromResult(originalPath + ".backup");
    }

    public string GetFileNameWithoutExtension(string path)
    {
        return Path.GetFileNameWithoutExtension(path);
    }

    public string GetDirectoryName(string path)
    {
        return Path.GetDirectoryName(path) ?? string.Empty;
    }
}