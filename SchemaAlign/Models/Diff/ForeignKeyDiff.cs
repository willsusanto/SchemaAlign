namespace SchemaAlign.Models.Diff;

public class ForeignKeyDiff
{
    public string ForeignKeyName { get; set; } = string.Empty;
    public DiffType DiffType { get; set; } = DiffType.None;
    public ForeignKeySchema? SourceForeignKey { get; set; }
    public ForeignKeySchema? TargetForeignKey { get; set; }
    public List<string> ChangeDescriptions { get; set; } = new();

    public bool HasChanges => DiffType != DiffType.None;

    public static string GetKey(ForeignKeySchema fk) =>
        !string.IsNullOrEmpty(fk.ConstraintName)
            ? fk.ConstraintName
            : $"{fk.DependentColumn}->{fk.PrincipalTable}.{fk.PrincipalColumn}";

    public static ForeignKeyDiff Added(ForeignKeySchema sourceFk) => new()
    {
        ForeignKeyName = GetKey(sourceFk),
        DiffType = DiffType.Added,
        SourceForeignKey = sourceFk,
        ChangeDescriptions = { $"Added foreign key on '{sourceFk.DependentColumn}' referencing '{sourceFk.PrincipalTable}.{sourceFk.PrincipalColumn}'." }
    };

    public static ForeignKeyDiff Deleted(ForeignKeySchema targetFk) => new()
    {
        ForeignKeyName = GetKey(targetFk),
        DiffType = DiffType.Deleted,
        TargetForeignKey = targetFk,
        ChangeDescriptions = { $"Deleted foreign key on '{targetFk.DependentColumn}' referencing '{targetFk.PrincipalTable}.{targetFk.PrincipalColumn}'." }
    };
}
