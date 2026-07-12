using System.IO;
using System.Text.Json;
using DAW.MVVM.Models;

namespace DAW.Services;

/// <summary>
/// Settings service implementation using JSON file storage
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private Dictionary<string, object> _settings;

    public SettingsService()
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "Lapis DAW");
            Directory.CreateDirectory(appFolder);
            
            _settingsPath = Path.Combine(appFolder, "settings.json");
            _settings = LoadSettings();
        }
        catch
        {
            // Fallback to temp directory or in-memory only
            _settingsPath = Path.Combine(Path.GetTempPath(), "lapis_daw_settings.json");
            _settings = new Dictionary<string, object>();
        }
    }

    public async Task<T> GetSettingAsync<T>(string key, T defaultValue)
    {
        return await Task.Run(() =>
        {
            if (_settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is JsonElement jsonElement)
                    {
                        return jsonElement.Deserialize<T>() ?? defaultValue;
                    }
                    return (T)Convert.ChangeType(value, typeof(T)) ?? defaultValue;
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        });
    }

    public async Task SetSettingAsync<T>(string key, T value)
    {
        await Task.Run(async () =>
        {
            _settings[key] = value!;
            await SaveSettingsAsync();
        });
    }

    public async Task<IList<RecentProject>> GetRecentProjectsAsync()
    {
        return await GetSettingAsync("RecentProjects", new List<RecentProject>());
    }

    public async Task SetRecentProjectsAsync(IList<RecentProject> recentProjects)
    {
        await SetSettingAsync("RecentProjects", recentProjects);
    }

    public async Task<IList<ProjectTemplate>> GetTemplatesAsync()
    {
        var templates = await GetSettingAsync("Templates", new List<ProjectTemplate>());
        
        // Add built-in templates if not present
        if (!templates.Any())
        {
            templates = new List<ProjectTemplate>
            {
                new() { Name = "Leeres Projekt", FilePath = "", Category = "Standard", Description = "Ein komplett leeres Projekt" },
                new() { Name = "Basic Beat", FilePath = "", Category = "Standard", Description = "Grundlegendes Beat-Template" },
                new() { Name = "Full Song", FilePath = "", Category = "Standard", Description = "Vollständige Song-Struktur" }
            };
            await SetTemplatesAsync(templates);
        }

        return templates;
    }

    public async Task SetTemplatesAsync(IList<ProjectTemplate> templates)
    {
        await SetSettingAsync("Templates", templates);
    }

    private Dictionary<string, object> LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
            }
        }
        catch
        {
            // If loading fails, start with empty settings
        }

        return new Dictionary<string, object>();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(_settings, options);
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch
        {
            // Handle save errors silently
        }
    }
}