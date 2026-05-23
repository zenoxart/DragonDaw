using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Imaging.Effects;
using System.Runtime.CompilerServices;
using DAW.Audio.Effects;

namespace DAW.Plugins;

/// <summary>
/// Represents a plugin definition that can be instantiated.
/// </summary>
public class PluginDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Icon { get; init; }
    public required string Description { get; init; }
    public required Func<AudioEffect> Factory { get; init; }
    public int UsageCount { get; set; }
    public DateTime LastUsed { get; set; }
    public string[] Tags { get; init; } = [];
    
    public string SearchText => $"{Name} {Category} {string.Join(" ", Tags)}".ToLowerInvariant();
}

/// <summary>
/// Manages all available plugins and their instances.
/// </summary>
public class PluginManager : INotifyPropertyChanged
{
    private static PluginManager? _instance;
    public static PluginManager Instance => _instance ??= new PluginManager();
    
    private readonly List<PluginDefinition> _plugins = [];
    private readonly List<PluginWindow> _openWindows = [];

    public IReadOnlyList<PluginDefinition> Plugins => _plugins;
    public IReadOnlyList<PluginWindow> OpenWindows => _openWindows;
    
    public ObservableCollection<PluginDefinition> RecentPlugins { get; } = [];
    public ObservableCollection<PluginDefinition> FavoritePlugins { get; } = [];

    private PluginManager()
    {
        RegisterBuiltInPlugins();
        Register(new PluginDefinition
        {
            Id = "lapis.spectre",
            Name = "Spectre",
            Category = "Saturation",
            Icon = "✨",
            Description = "Parallel multiband saturator with 5 bands, 11 algorithms and M/S processing",
            Factory = () => new SpectreEffect(),
            Tags = ["spectre", "multiband", "saturator", "enhancer", "harmonic", "exciter", "parallel", "ms"]
        });
        Register(new PluginDefinition
        {
            Id = "lapis.dragonparticle",
            Name = "Dragon Particle",
            Category = "Mastering",
            Icon = "🐉",
            Description = "Intelligent one-knob mastering: multiband compression, EQ, saturation, limiting",
            Factory = () => new MasterEffect(),
            Tags = ["dragon", "particle", "master", "mastering", "limiter", "compressor", "glue", "loudness", "bus"]
        });
    }

    private void RegisterBuiltInPlugins()
    {
        Register(new PluginDefinition
        {
            Id = "lapis.eq3",
            Name = "Parametric EQ",
            Category = "Equalizer",
            Icon = "📊",
            Description = "3-Band parametric equalizer with Low, Mid, High controls",
            Factory = () => new EqualizerEffect(),
            Tags = ["eq", "equalizer", "filter", "tone", "bass", "treble"]
        });

        Register(new PluginDefinition
        {
            Id = "lapis.compressor",
            Name = "1176 Compressor",
            Category = "Dynamics",
            Icon = "📉",
            Description = "Dynamic range compressor with attack, release, and makeup gain",
            Factory = () => new CompressorEffect(),
            Tags = ["compressor", "dynamics", "limiter", "squeeze", "punch"]
        });

        Register(new PluginDefinition
        {
            Id = "lapis.reverb",
            Name = "Reverb",
            Category = "Reverb",
            Icon = "🏛️",
            Description = "Algorithmic reverb with room size and damping controls",
            Factory = () => new ReverbEffect(),
            Tags = ["reverb", "hall", "room", "space", "ambience", "echo"]
        });

        Register(new PluginDefinition
        {
            Id = "lapis.delay",
            Name = "Delay",
            Category = "Delay",
            Icon = "🔁",
            Description = "Stereo delay with feedback and ping-pong mode",
            Factory = () => new DelayEffect(),
            Tags = ["delay", "echo", "repeat", "pingpong", "stereo"]
        });

        Register(new PluginDefinition
        {
            Id = "lapis.gain",
            Name = "Gain",
            Category = "Utility",
            Icon = "🔊",
            Description = "Volume control with optional soft clipping/saturation",
            Factory = () => new GainEffect(),
            Tags = ["gain", "volume", "utility", "saturation", "clip"]
        });

        Register(new PluginDefinition
        {
            Id = "lapis.blackbox",
            Name = "Blackbox",
            Category = "Utility",
            Icon = "🔊",
            Description = "Tube Saturation",
            Factory = () => new SaturationEffect(),
            Tags = ["tube", "blackbox", "utility", "saturation"]
        });
    }

    public void Register(PluginDefinition plugin)
    {
        _plugins.Add(plugin);
    }

    /// <summary>
    /// Creates a plugin instance and opens it in a new window.
    /// </summary>
    public PluginWindow? OpenPlugin(PluginDefinition definition, Models.Track? targetTrack = null)
    {
        try
        {
            var effect = definition.Factory();
            
            // Update usage statistics
            definition.UsageCount++;
            definition.LastUsed = DateTime.Now;
            UpdateRecentPlugins(definition);
            
            // Create window (don't add effect to track yet - window handles this)
            var window = new PluginWindow(effect, definition, targetTrack);
            
            // Set owner to main window if available
            if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } mainWindow 
                && mainWindow.IsVisible)
            {
                window.Owner = mainWindow;
            }
            
            window.Closed += (s, e) => _openWindows.Remove(window);
            _openWindows.Add(window);
            window.Show();
            
            OnPropertyChanged(nameof(OpenWindows));
            return window;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open plugin: {ex.Message}");
            return null;
        }
    }

    private void UpdateRecentPlugins(PluginDefinition plugin)
    {
        RecentPlugins.Remove(plugin);
        RecentPlugins.Insert(0, plugin);
        
        while (RecentPlugins.Count > 10)
            RecentPlugins.RemoveAt(RecentPlugins.Count - 1);
    }

    /// <summary>
    /// Fuzzy search for plugins.
    /// </summary>
    public IEnumerable<PluginDefinition> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            // Return favorites first, then recent, then by usage
            return FavoritePlugins
                .Concat(RecentPlugins.Except(FavoritePlugins))
                .Concat(_plugins.Except(FavoritePlugins).Except(RecentPlugins)
                    .OrderByDescending(p => p.UsageCount));
        }

        var lowerQuery = query.ToLowerInvariant();
        var terms = lowerQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return _plugins
            .Select(p => new { Plugin = p, Score = CalculateMatchScore(p, terms) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Plugin.UsageCount)
            .Select(x => x.Plugin);
    }

    private static int CalculateMatchScore(PluginDefinition plugin, string[] terms)
    {
        int score = 0;
        var searchText = plugin.SearchText;
        var name = plugin.Name.ToLowerInvariant();

        foreach (var term in terms)
        {
            // Exact name match = highest score
            if (name == term) score += 100;
            // Name starts with term
            else if (name.StartsWith(term)) score += 50;
            // Name contains term
            else if (name.Contains(term)) score += 30;
            // Category match
            else if (plugin.Category.ToLowerInvariant().Contains(term)) score += 20;
            // Tag/search text contains term
            else if (searchText.Contains(term)) score += 10;
            // Fuzzy match (Levenshtein distance <= 2)
            else if (FuzzyMatch(term, name)) score += 5;
            else return 0; // Term not found at all
        }

        return score;
    }

    private static bool FuzzyMatch(string term, string target)
    {
        // Simple fuzzy matching - check if all characters appear in order
        int termIndex = 0;
        foreach (var c in target)
        {
            if (termIndex < term.Length && c == term[termIndex])
                termIndex++;
        }
        return termIndex >= term.Length - 1; // Allow 1 missing char
    }

    public void CloseAllPlugins()
    {
        foreach (var window in _openWindows.ToList())
        {
            window.Close();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
