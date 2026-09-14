namespace SchemaAlign.Readers.Mermaid;

/// <summary>
/// Configuration options for Mermaid ER diagram parsing.
/// </summary>
public class MermaidReaderOptions
{
    /// <summary>
    /// Known table prefixes to strip when matching candidate foreign key columns (e.g. "tbl_", "px_").
    /// </summary>
    public List<string> TablePrefixes { get; set; } = new();
}
