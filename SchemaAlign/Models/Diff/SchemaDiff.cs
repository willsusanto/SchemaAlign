namespace SchemaAlign.Models.Diff;

public class SchemaDiff
{
    public DatabaseSchema? SourceSchema { get; set; }
    public DatabaseSchema? TargetSchema { get; set; }
    public Dictionary<string, TableDiff> TableDiffs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool HasChanges => TableDiffs.Values.Any(t => t.HasChanges);

    public IEnumerable<TableDiff> AddedTables =>
        TableDiffs.Values.Where(t => t.DiffType == DiffType.Added);

    public IEnumerable<TableDiff> ModifiedTables =>
        TableDiffs.Values.Where(t => t.DiffType == DiffType.Modified);

    public IEnumerable<TableDiff> DeletedTables =>
        TableDiffs.Values.Where(t => t.DiffType == DiffType.Deleted);

    public void AddTableDiff(TableDiff tableDiff)
    {
        TableDiffs[tableDiff.TableName] = tableDiff;
    }

    public static SchemaDiff Compare(DatabaseSchema source, DatabaseSchema target)
    {
        var diff = new SchemaDiff
        {
            SourceSchema = source,
            TargetSchema = target
        };

        // Check tables in source against target
        foreach (var (tableName, srcTable) in source.Tables)
        {
            if (target.Tables.TryGetValue(tableName, out var tgtTable))
            {
                var tableDiff = TableDiff.Compare(srcTable, tgtTable);
                if (tableDiff != null)
                {
                    diff.TableDiffs[tableName] = tableDiff;
                }
            }
            else
            {
                diff.TableDiffs[tableName] = TableDiff.Added(srcTable);
            }
        }

        // Check tables in target deleted from source
        foreach (var (tableName, tgtTable) in target.Tables)
        {
            if (!source.Tables.ContainsKey(tableName))
            {
                diff.TableDiffs[tableName] = TableDiff.Deleted(tgtTable);
            }
        }

        return diff;
    }
}
