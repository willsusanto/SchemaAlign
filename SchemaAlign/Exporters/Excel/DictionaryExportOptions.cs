namespace SchemaAlign.Exporters.Excel;

/// <summary>
/// Configuration options for generating an Excel data dictionary spreadsheet.
/// </summary>
public class DictionaryExportOptions
{
    public const string DefaultAid = "1191";
    public const string DefaultIpDomain = "ssg5-newlibrary-dev.binus.db";
    public const string DefaultDatabaseName = "LIBRARY_DB";
    public const string DefaultSystemTitle = "New Library System";

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
}
