namespace SchemaAlign.Appliers;

/// <summary>
/// Represents the outcome of applying a schema diff.
/// </summary>
public class ApplierResult
{
    /// <summary>
    /// Indicates whether the diff was applied successfully without unhandled errors.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// List of file paths modified on disk.
    /// </summary>
    public List<string> ChangedFiles { get; set; } = new();

    /// <summary>
    /// List of file paths newly created on disk.
    /// </summary>
    public List<string> CreatedFiles { get; set; } = new();

    /// <summary>
    /// List of file paths deleted from disk.
    /// </summary>
    public List<string> DeletedFiles { get; set; } = new();

    /// <summary>
    /// List of errors encountered during application.
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// File diff previews generated for the applied changes.
    /// </summary>
    public List<FileDiffPreview> Previews { get; set; } = new();
}
