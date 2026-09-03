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

    /// <summary>
    /// Source directories containing existing entity files for cross-directory type resolution.
    /// </summary>
    public List<string> SourceDirectories { get; set; } = new();

    /// <summary>
    /// Known entity class names mapped to their declared namespaces.
    /// </summary>
    public Dictionary<string, string> EntityNamespaces { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Additional using namespace directives to add to generated entity files.
    /// </summary>
    public List<string> AdditionalUsings { get; set; } = new();

    /// <summary>
    /// Whether to automatically detect the namespace of existing entity files in the target directory. Defaults to true.
    /// </summary>
    public bool AutoDetectNamespace { get; set; } = true;
}


