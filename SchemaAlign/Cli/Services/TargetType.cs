namespace SchemaAlign.Cli.Services;

/// <summary>
/// Specifies the format or technology of a schema source or target.
/// </summary>
public enum TargetType
{
    Unknown = 0,
    Mermaid,
    CSharp,
    SqlServerScript,
    SqlServerDatabase
}
