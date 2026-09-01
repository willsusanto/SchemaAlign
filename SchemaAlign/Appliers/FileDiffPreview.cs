using SchemaAlign.Diff;

namespace SchemaAlign.Appliers;

/// <summary>
/// Represents a preview of file changes resulting from a schema diff application.
/// </summary>
public class FileDiffPreview
{
    /// <summary>
    /// Target file path.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Kind of modification (Added, Modified, Deleted, Unchanged).
    /// </summary>
    public DiffKind DiffKind { get; set; } = DiffKind.Unchanged;

    /// <summary>
    /// Original file content before modification, or null if file is newly added.
    /// </summary>
    public string? OriginalContent { get; set; }

    /// <summary>
    /// New file content after modification, or null if file is deleted.
    /// </summary>
    public string? NewContent { get; set; }

    /// <summary>
    /// Standard unified diff representation (git format) of the changes.
    /// </summary>
    public string UnifiedDiff { get; set; } = string.Empty;
}
