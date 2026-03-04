namespace DAW.Models;

/// <summary>
/// Model representing a recent project entry
/// </summary>
public class RecentProject
{
    public string FilePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }
    public bool Exists { get; set; } = true;
}

/// <summary>
/// Model representing project templates
/// </summary>
public class ProjectTemplate
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}