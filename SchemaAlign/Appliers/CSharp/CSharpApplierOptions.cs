namespace SchemaAlign.Appliers.CSharp;

/// <summary>
/// Options for configuring C# entity generation and AST rewriting.
/// </summary>
public class CSharpApplierOptions : ApplierOptions
{
    /// <summary>
    /// Default namespace for newly generated C# entity files. Defaults to "Entities".
    /// </summary>
    public string? DefaultNamespace { get; set; } = "Entities";

    /// <summary>
    /// Whether to use file-scoped namespace declarations (C# 10+). Defaults to true.
    /// </summary>
    public bool UseFileScopedNamespaces { get; set; } = true;

    /// <summary>
    /// Whether to generate DataAnnotation attributes ([Key], [Column], [Table], etc.). Defaults to true.
    /// </summary>
    public bool UseDataAnnotations { get; set; } = true;

    /// <summary>
    /// Whether to enable nullable reference type annotations and default initializers. Defaults to true.
    /// </summary>
    public bool UseNullableReferenceTypes { get; set; } = true;

    /// <summary>
    /// Whether to include Schema in the [Table] attribute (e.g. [Table("Name", Schema = "dbo")]). Defaults to false.
    /// </summary>
    public bool AddSchemaToTableAttribute { get; set; } = false;

    /// <summary>
    /// Whether to automatically add missing using directives when DataAnnotation attributes are introduced. Defaults to true.
    /// </summary>
    public bool AutoAddMissingUsings { get; set; } = true;

    /// <summary>
    /// Whether to delete entity files corresponding to dropped/deleted tables. Defaults to false.
    /// </summary>
    public bool DeleteDroppedTables { get; set; } = false;
}

