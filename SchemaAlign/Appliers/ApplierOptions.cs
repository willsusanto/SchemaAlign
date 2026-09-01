namespace SchemaAlign.Appliers;

/// <summary>
/// Base options for schema appliers.
/// </summary>
public class ApplierOptions
{
    /// <summary>
    /// Target directory where output source files reside or will be generated.
    /// </summary>
    public string TargetDirectory { get; set; } = string.Empty;

    /// <summary>
    /// If true, destructive changes (dropping tables, dropping columns) are allowed. Default is false.
    /// </summary>
    public bool AllowDrops { get; set; } = false;

    /// <summary>
    /// If true, runs the applier in dry-run mode without modifying files on disk or executing migrations.
    /// </summary>
    public bool DryRun { get; set; } = false;
}
