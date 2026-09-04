using SchemaAlign.Configuration;

namespace SchemaAlign.Exporters.Excel;

/// <summary>
/// Configuration options for generating an Excel data dictionary spreadsheet.
/// </summary>
public class DictionaryExportOptions
{
    public const string DefaultAid = "-";
    public const string DefaultIpDomain = "-";
    public const string DefaultDatabaseName = "DATABASE";
    public const string DefaultSystemTitle = "Data Dictionary";

    public string SourcePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string Aid { get; set; } = DefaultAid;
    public string IpDomain { get; set; } = DefaultIpDomain;
    public string DatabaseName { get; set; } = DefaultDatabaseName;
    public string SystemTitle { get; set; } = DefaultSystemTitle;
    public string? ConfigPath { get; set; }
    /// <summary>
    /// Set of table class names to highlight with a background color in the export.
    /// Defaults to "newTbl" and "updatedTbl".
    /// </summary>
    public HashSet<string> HighlightClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "newTbl", "updatedTbl" };

    /// <summary>
    /// Hex color code for the background highlight of matching classified tables.
    /// Defaults to "#ffcccc".
    /// </summary>
    public string HighlightColor { get; set; } = "#ffcccc";

    /// <summary>
    /// Pre-configured notes and sample data mapped by column name (case-insensitive).
    /// </summary>
    public Dictionary<string, DictionaryColumnDefault> ColumnDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
