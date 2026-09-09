using System.Text.Json;

namespace SchemaAlign.Configuration;

/// <summary>
/// Discovers and loads SchemaAlign configuration from .schemaalign.json or custom paths.
/// </summary>
public static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] ConfigFileNames = { ".schemaalign.json", "schemaalign.json" };

    /// <summary>
    /// Parses a JSON string into a <see cref="SchemaAlignConfiguration"/> instance.
    /// </summary>
    public static SchemaAlignConfiguration Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SchemaAlignConfiguration();
        }

        return JsonSerializer.Deserialize<SchemaAlignConfiguration>(json, JsonOptions)
            ?? new SchemaAlignConfiguration();
    }

    /// <summary>
    /// Searches upward from the specified start directory to locate a configuration file.
    /// Stops at git root (.git folder) or the filesystem root.
    /// </summary>
    public static string? FindConfigFile(string? startDirectory = null)
    {
        var current = string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(startDirectory);

        while (!string.IsNullOrEmpty(current) && Directory.Exists(current))
        {
            foreach (var name in ConfigFileNames)
            {
                var candidate = Path.Combine(current, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Check if current directory has .git (repository root)
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
            {
                // Searched git root; stop searching higher
                break;
            }

            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return null;
    }

    /// <summary>
    /// Loads a configuration from a specific file path.
    /// </summary>
    public static async Task<SchemaAlignConfiguration?> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        return Parse(json);
    }

    /// <summary>
    /// Locates the nearest configuration file upward from start directory and loads it.
    /// </summary>
    public static async Task<SchemaAlignConfiguration?> FindAndLoadAsync(string? startDirectory = null, CancellationToken cancellationToken = default)
    {
        var path = FindConfigFile(startDirectory);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return await LoadAsync(path, cancellationToken);
    }
}
