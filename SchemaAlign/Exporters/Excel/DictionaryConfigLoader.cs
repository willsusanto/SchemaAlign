using System.Text.Json;

namespace SchemaAlign.Exporters.Excel;

/// <summary>
/// Loads and merges dictionary export configuration from schemaalign.json and CLI arguments.
/// </summary>
public static class DictionaryConfigLoader
{
    private class ConfigFileModel
    {
        public DictionarySection? Dictionary { get; set; }
        public string? Aid { get; set; }
        public string? Ip { get; set; }
        public string? Database { get; set; }
        public string? SystemTitle { get; set; }
    }

    private class DictionarySection
    {
        public string? Aid { get; set; }
        public string? Ip { get; set; }
        public string? Database { get; set; }
        public string? SystemTitle { get; set; }
    }

    public static DictionaryExportOptions Load(
        string? configPath,
        string? cliAid,
        string? cliIp,
        string? cliDb,
        string? cliTitle,
        string sourcePath,
        string outputPath)
    {
        var options = new DictionaryExportOptions
        {
            SourcePath = sourcePath,
            OutputPath = outputPath,
            ConfigPath = configPath
        };

        // Resolve config file path: explicit path or default ./schemaalign.json
        var resolvedConfigPath = configPath;
        if (string.IsNullOrWhiteSpace(resolvedConfigPath))
        {
            var defaultFile = Path.Combine(Directory.GetCurrentDirectory(), "schemaalign.json");
            if (File.Exists(defaultFile))
            {
                resolvedConfigPath = defaultFile;
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedConfigPath) && File.Exists(resolvedConfigPath))
        {
            try
            {
                var json = File.ReadAllText(resolvedConfigPath);
                var doc = JsonSerializer.Deserialize<ConfigFileModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (doc != null)
                {
                    var section = doc.Dictionary;
                    var cfgAid = section?.Aid ?? doc.Aid;
                    var cfgIp = section?.Ip ?? doc.Ip;
                    var cfgDb = section?.Database ?? doc.Database;
                    var cfgTitle = section?.SystemTitle ?? doc.SystemTitle;

                    if (!string.IsNullOrWhiteSpace(cfgAid)) options.Aid = cfgAid;
                    if (!string.IsNullOrWhiteSpace(cfgIp)) options.IpDomain = cfgIp;
                    if (!string.IsNullOrWhiteSpace(cfgDb)) options.DatabaseName = cfgDb;
                    if (!string.IsNullOrWhiteSpace(cfgTitle)) options.SystemTitle = cfgTitle;
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
