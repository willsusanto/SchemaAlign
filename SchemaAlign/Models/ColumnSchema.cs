namespace SchemaAlign.Models;

public class ColumnSchema
{
    public string Name { get; set; } = string.Empty;
    public StandardType Type { get; set; } = StandardType.Unknown;
    public string? RawType { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public string? DefaultValue { get; set; }
    public string? Comment { get; set; }
}
