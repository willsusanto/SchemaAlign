namespace SchemaAlign.Readers.Mermaid;

/// <summary>
/// Configuration options for Mermaid ER diagram parsing and convention resolution.
/// </summary>
public class MermaidReaderOptions
{
    /// <summary>
    /// Known table prefixes (e.g. "Ms", "Tr", "Lt", "Tb", "tbl_") to strip when matching convention-based foreign key columns.
    /// </summary>
    public List<string> TablePrefixes { get; set; } = new() { "Ms", "Tr", "Lt", "Tb" };
}
