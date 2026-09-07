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
    public HashSet<string> HighlightClasses { get; set; } = new(StringComparer.OrdinalIgnoreCase) { "newTbl", "updatedTbl" };
    public string HighlightColor { get; set; } = "#ffcccc";
}
