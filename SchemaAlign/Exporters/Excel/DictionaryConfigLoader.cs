using SchemaAlign.Configuration;

namespace SchemaAlign.Exporters.Excel;

/// <summary>
/// Loads and merges dictionary export configuration from .schemaalign.json and CLI arguments.
/// </summary>
public static class DictionaryConfigLoader
{
    /// <summary>
    /// Loads and merges dictionary export options from a configuration file and CLI flags.
    /// </summary>
    public static DictionaryExportOptions Load(
        string? configPath,
        string? cliAid,
        string? cliIp,
        string? cliDb,
        string? cliTitle,
        string sourcePath,
        string outputPath,
        string? searchDirectory = null)
    {
        var options = new DictionaryExportOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            ConfigPath = configPath
        };

        // Resolve config file path: explicit path or upward discovery via ConfigurationLoader
        var resolvedConfigPath = !string.IsNullOrWhiteSpace(configPath)
            ? configPath
            : ConfigurationLoader.FindConfigFile(searchDirectory);

        if (!string.IsNullOrWhiteSpace(resolvedConfigPath) && File.Exists(resolvedConfigPath))
        {
            try
            {
                var json = File.ReadAllText(resolvedConfigPath);
                var config = ConfigurationLoader.Parse(json);

                if (config.Dictionary != null)
                {
                    if (!string.IsNullOrWhiteSpace(config.Dictionary.Aid)) options.Aid = config.Dictionary.Aid;
                    if (!string.IsNullOrWhiteSpace(config.Dictionary.Ip)) options.IpDomain = config.Dictionary.Ip;
                    if (!string.IsNullOrWhiteSpace(config.Dictionary.Database)) options.DatabaseName = config.Dictionary.Database;
                    if (!string.IsNullOrWhiteSpace(config.Dictionary.SystemTitle)) options.SystemTitle = config.Dictionary.SystemTitle;
                    if (config.Dictionary.HighlightClasses != null)
                    {
                        options.HighlightClasses = new HashSet<string>(config.Dictionary.HighlightClasses, StringComparer.OrdinalIgnoreCase);
                    }
                    if (!string.IsNullOrWhiteSpace(config.Dictionary.HighlightColor))
                    {
                        options.HighlightColor = config.Dictionary.HighlightColor;
                    }

                    foreach (var (colName, colDefault) in config.Dictionary.ColumnDefaults)
                    {
                        options.ColumnDefaults[colName] = colDefault;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Failed to parse configuration file '{resolvedConfigPath}': {ex.Message}");
            }
        }

        // CLI flags take highest precedence
        if (!string.IsNullOrWhiteSpace(cliAid)) options.Aid = cliAid;
        if (!string.IsNullOrWhiteSpace(cliIp)) options.IpDomain = cliIp;
        if (!string.IsNullOrWhiteSpace(cliDb)) options.DatabaseName = cliDb;
        if (!string.IsNullOrWhiteSpace(cliTitle)) options.SystemTitle = cliTitle;

        return options;
    }
}
