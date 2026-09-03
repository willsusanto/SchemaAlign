namespace SchemaAlign.Configuration;

/// <summary>
/// Root configuration settings for SchemaAlign, typically loaded from .schemaalign.json.
/// </summary>
public class SchemaAlignConfiguration
{
    /// <summary>
    /// Path to current/base schema (file, directory, or connection string).
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Path to desired/target schema (e.g. .mmd diagram or new spec).
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Diff mode: 'incremental' or 'snapshot'.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Whether destructive drops (DROP TABLE, DROP COLUMN) are permitted.
    /// </summary>
    public bool? AllowDrop { get; set; }

    /// <summary>
    /// Output format ('console', 'json', 'markdown').
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// Whether to display detailed property-level change trees.
    /// </summary>
    public bool? Detailed { get; set; }

    /// <summary>
    /// C# entity generation and applier specific configurations.
    /// </summary>
    public CSharpConfiguration CSharp { get; set; } = new();
}

/// <summary>
/// C# entity applier and generator configuration settings.
/// </summary>
public class CSharpConfiguration
{
    /// <summary>
    /// Target namespace for newly generated C# entity files.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Base class for newly generated C# entity classes (e.g. "AuditEntity" or "BaseModel").
    /// </summary>
    public string? BaseClass { get; set; }

    /// <summary>
    /// Additional using namespace directives to include at the top of generated C# files.
    /// </summary>
    public List<string> Usings { get; set; } = new();

    /// <summary>
    /// Custom class-level attributes to emit on generated C# classes (e.g. "[DatabaseName(\"LibraryDB\")]").
    /// </summary>
    public List<string> ClassAttributes { get; set; } = new();

    /// <summary>
    /// Column names to omit from the class body because they are inherited from the base class.
    /// </summary>
    public List<string> OmitInheritedColumns { get; set; } = new();

    /// <summary>
    /// Whether to use file-scoped namespace declarations (defaults to true).
    /// </summary>
    public bool? UseFileScopedNamespaces { get; set; }

    /// <summary>
    /// Whether to generate DataAnnotation attributes ([Key], [Column], [Table], etc.).
    /// </summary>
    public bool? UseDataAnnotations { get; set; }
}
