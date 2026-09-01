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
}
