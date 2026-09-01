namespace SchemaAlign.Models.Diff;

public class ColumnDiff
{
    public string ColumnName { get; set; } = string.Empty;
    public DiffType DiffType { get; set; } = DiffType.None;
    public ColumnSchema? SourceColumn { get; set; }
    public ColumnSchema? TargetColumn { get; set; }
    public List<string> ChangeDescriptions { get; set; } = new();

    public bool HasChanges => DiffType != DiffType.None;

    public static ColumnDiff Added(ColumnSchema sourceColumn) => new()
    {
        ColumnName = sourceColumn.Name,
        DiffType = DiffType.Added,
        SourceColumn = sourceColumn,
        ChangeDescriptions = { $"Added column '{sourceColumn.Name}' of type '{sourceColumn.Type}'." }
    };

    public static ColumnDiff Deleted(ColumnSchema targetColumn) => new()
    {
        ColumnName = targetColumn.Name,
        DiffType = DiffType.Deleted,
        TargetColumn = targetColumn,
        ChangeDescriptions = { $"Deleted column '{targetColumn.Name}'." }
    };

    public static ColumnDiff? Compare(ColumnSchema source, ColumnSchema target)
    {
        var changes = new List<string>();

        if (source.Type != target.Type)
        {
            changes.Add($"Type changed from '{target.Type}' to '{source.Type}'.");
        }

        if (source.IsNullable != target.IsNullable)
        {
            changes.Add($"Nullability changed from '{target.IsNullable}' to '{source.IsNullable}'.");
        }

        if (source.Length != target.Length)
        {
            changes.Add($"Length changed from '{target.Length}' to '{source.Length}'.");
        }

        if (source.Precision != target.Precision || source.Scale != target.Scale)
        {
            changes.Add($"Precision/Scale changed from '({target.Precision},{target.Scale})' to '({source.Precision},{source.Scale})'.");
        }

        if (source.IsPrimaryKey != target.IsPrimaryKey)
        {
            changes.Add($"Primary key status changed from '{target.IsPrimaryKey}' to '{source.IsPrimaryKey}'.");
        }

        if (source.IsIdentity != target.IsIdentity)
        {
            changes.Add($"Identity status changed from '{target.IsIdentity}' to '{source.IsIdentity}'.");
        }

        if (source.DefaultValue != target.DefaultValue)
        {
            changes.Add($"Default value changed from '{target.DefaultValue}' to '{source.DefaultValue}'.");
        }

        if (source.Comment != target.Comment && !string.IsNullOrEmpty(source.Comment))
        {
            changes.Add($"Comment updated.");
        }

        if (changes.Count == 0)
        {
            return null;
        }

        return new ColumnDiff
        {
            ColumnName = source.Name,
            DiffType = DiffType.Modified,
            SourceColumn = source,
            TargetColumn = target,
            ChangeDescriptions = changes
        };
    }
}
