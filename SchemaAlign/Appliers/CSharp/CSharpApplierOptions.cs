namespace SchemaAlign.Appliers.CSharp;

public class CSharpApplierOptions : ApplierOptions
{
    public string? DefaultNamespace { get; set; } = "Entities";
    public bool UseFileScopedNamespaces { get; set; } = true;
    public bool UseDataAnnotations { get; set; } = true;
    public bool UseNullableReferenceTypes { get; set; } = true;
    public bool AddSchemaToTableAttribute { get; set; } = false;
    public bool AutoAddMissingUsings { get; set; } = true;
    public bool DeleteDroppedTables { get; set; } = false;
}

