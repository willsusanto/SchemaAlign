using SchemaAlign.Models;

namespace SchemaAlign.Diff;

public enum DiffKind
{
    Unchanged = 0,
    Added,
    Modified,
    Deleted
}

[Flags]
public enum ChangeDetail
{
    None = 0,
    TypeChanged = 1 << 0,
    LengthChanged = 1 << 1,
    NullabilityChanged = 1 << 2,
    KeyStatusChanged = 1 << 3,
    PrecisionChanged = 1 << 4,
    ScaleChanged = 1 << 5,
    IdentityChanged = 1 << 6,
    DefaultValueChanged = 1 << 7,
    CommentChanged = 1 << 8
}

public class ColumnDiff
{
    public string ColumnName { get; set; } = string.Empty;
    public DiffKind Kind { get; set; } = DiffKind.Unchanged;
    public ColumnSchema? Source { get; set; }
    public ColumnSchema? Target { get; set; }
    public ChangeDetail Changes { get; set; } = ChangeDetail.None;
}

public class ForeignKeyDiff
{
    public string? ConstraintName { get; set; }
    public DiffKind Kind { get; set; } = DiffKind.Unchanged;
    public ForeignKeySchema? Source { get; set; }
    public ForeignKeySchema? Target { get; set; }
    public bool CardinalityChanged { get; set; }
}

public class TableDiff
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public DiffKind Kind { get; set; } = DiffKind.Unchanged;
    public TableSchema? Source { get; set; }
    public TableSchema? Target { get; set; }
    public List<ColumnDiff> Columns { get; set; } = new();
    public List<ForeignKeyDiff> ForeignKeys { get; set; } = new();

    public bool HasChanges => Kind != DiffKind.Unchanged;

    public IEnumerable<ColumnDiff> AddedColumns => Columns.Where(c => c.Kind == DiffKind.Added);
    public IEnumerable<ColumnDiff> ModifiedColumns => Columns.Where(c => c.Kind == DiffKind.Modified);
    public IEnumerable<ColumnDiff> DeletedColumns => Columns.Where(c => c.Kind == DiffKind.Deleted);

    public IEnumerable<ForeignKeyDiff> AddedForeignKeys => ForeignKeys.Where(f => f.Kind == DiffKind.Added);
    public IEnumerable<ForeignKeyDiff> ModifiedForeignKeys => ForeignKeys.Where(f => f.Kind == DiffKind.Modified);
    public IEnumerable<ForeignKeyDiff> DeletedForeignKeys => ForeignKeys.Where(f => f.Kind == DiffKind.Deleted);
}

public class SchemaDiff
{
    public DatabaseSchema SourceSchema { get; set; } = new();
    public DatabaseSchema TargetSchema { get; set; } = new();
    public List<TableDiff> Tables { get; set; } = new();

    public bool HasChanges => Tables.Any(t => t.HasChanges);

    public IEnumerable<TableDiff> AddedTables => Tables.Where(t => t.Kind == DiffKind.Added);
    public IEnumerable<TableDiff> ModifiedTables => Tables.Where(t => t.Kind == DiffKind.Modified);
    public IEnumerable<TableDiff> DeletedTables => Tables.Where(t => t.Kind == DiffKind.Deleted);
    public IEnumerable<TableDiff> UnchangedTables => Tables.Where(t => t.Kind == DiffKind.Unchanged);

    public TableDiff? FindTable(string tableName) =>
        Tables.FirstOrDefault(t => string.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase));
}

public class SchemaDiffOptions
{
    /// <summary>
    /// If true, tables present in source but omitted from target are ignored (sprint incremental mode).
    /// If false, omitted tables are marked as DiffKind.Deleted (full snapshot mode).
    /// Default is true.
    /// </summary>
    public bool IgnoreOmittedTables { get; set; } = true;

    /// <summary>
    /// If true, foreign keys present in source but omitted from target are ignored (preserved).
    /// If false, omitted foreign keys are marked as DiffKind.Deleted.
    /// Default is true.
    /// </summary>
    public bool IgnoreOmittedForeignKeys { get; set; } = true;

    public static SchemaDiffOptions Incremental => new() { IgnoreOmittedTables = true, IgnoreOmittedForeignKeys = true };
    public static SchemaDiffOptions FullSnapshot => new() { IgnoreOmittedTables = false, IgnoreOmittedForeignKeys = false };
}
