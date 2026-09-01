namespace SchemaAlign.Models;

public class ForeignKeySchema
{
    public string? ConstraintName { get; set; }
    public string PrincipalTable { get; set; } = string.Empty;
    public string PrincipalColumn { get; set; } = "Id";
    public string DependentTable { get; set; } = string.Empty;
    public string DependentColumn { get; set; } = string.Empty;
    public ForeignKeyCardinality Cardinality { get; set; } = ForeignKeyCardinality.ManyToOne;
}
