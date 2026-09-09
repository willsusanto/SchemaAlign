namespace SchemaAlign.Appliers.SqlServer;

/// <summary>
/// Options for configuring SQL Server migration script generation and execution.
/// </summary>
public class SqlServerApplierOptions : ApplierOptions
{
    /// <summary>
    /// File name for generated migration script (defaults to "migration.sql").
    /// </summary>
    public string ScriptFileName { get; set; } = "migration.sql";

    /// <summary>
    /// Optional live database connection string. If specified, overrides TargetDirectory for direct execution.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Optional explicit path to output the generated .sql migration script to disk (even when ConnectionString is present).
    /// </summary>
    public string? ScriptOutputFilePath { get; set; }

    /// <summary>
    /// Whether to wrap migration operations in a transaction block. Default is true.
    /// </summary>
    public bool Transactional { get; set; } = true;

    /// <summary>
    /// Whether to include generation metadata header comments. Default is true.
    /// </summary>
    public bool IncludeHeader { get; set; } = true;

    /// <summary>
    /// Whether to generate idempotent DDL with existence checks (IF NOT EXISTS, OBJECT_ID). Default is true.
    /// </summary>
    public bool Idempotent { get; set; } = true;

    /// <summary>
    /// Default schema name when not specified (defaults to "dbo").
    /// </summary>
    public string DefaultSchema { get; set; } = "dbo";
}
