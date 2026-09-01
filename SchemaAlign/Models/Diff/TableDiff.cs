namespace SchemaAlign.Models.Diff;

public class TableDiff
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public DiffType DiffType { get; set; } = DiffType.None;
    public TableSchema? SourceTable { get; set; }
    public TableSchema? TargetTable { get; set; }
    public Dictionary<string, ColumnDiff> ColumnDiffs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ForeignKeyDiff> ForeignKeyDiffs { get; set; } = new();

    public bool HasChanges =>
        DiffType != DiffType.None ||
        ColumnDiffs.Values.Any(c => c.HasChanges) ||
        ForeignKeyDiffs.Any(f => f.HasChanges);

    public IEnumerable<ColumnDiff> AddedColumns =>
        ColumnDiffs.Values.Where(c => c.DiffType == DiffType.Added);

    public IEnumerable<ColumnDiff> ModifiedColumns =>
        ColumnDiffs.Values.Where(c => c.DiffType == DiffType.Modified);

    public IEnumerable<ColumnDiff> DeletedColumns =>
        ColumnDiffs.Values.Where(c => c.DiffType == DiffType.Deleted);

    public static TableDiff Added(TableSchema sourceTable)
    {
        var diff = new TableDiff
        {
            TableName = sourceTable.Name,
            Schema = sourceTable.Schema,
            DiffType = DiffType.Added,
            SourceTable = sourceTable
        };

        foreach (var col in sourceTable.Columns.Values)
        {
            diff.ColumnDiffs[col.Name] = ColumnDiff.Added(col);
        }

        foreach (var fk in sourceTable.ForeignKeys)
        {
            diff.ForeignKeyDiffs.Add(ForeignKeyDiff.Added(fk));
        }

        return diff;
    }

    public static TableDiff Deleted(TableSchema targetTable)
    {
        var diff = new TableDiff
        {
            TableName = targetTable.Name,
            Schema = targetTable.Schema,
            DiffType = DiffType.Deleted,
            TargetTable = targetTable
        };

        foreach (var col in targetTable.Columns.Values)
        {
            diff.ColumnDiffs[col.Name] = ColumnDiff.Deleted(col);
        }

        foreach (var fk in targetTable.ForeignKeys)
        {
            diff.ForeignKeyDiffs.Add(ForeignKeyDiff.Deleted(fk));
        }

        return diff;
    }

    public static TableDiff? Compare(TableSchema source, TableSchema target)
    {
        var diff = new TableDiff
        {
            TableName = source.Name,
            Schema = source.Schema,
            SourceTable = source,
            TargetTable = target
        };

        // Check columns in source against target
        foreach (var (colName, srcCol) in source.Columns)
        {
            if (target.Columns.TryGetValue(colName, out var tgtCol))
            {
                var colDiff = ColumnDiff.Compare(srcCol, tgtCol);
                if (colDiff != null)
                {
                    diff.ColumnDiffs[colName] = colDiff;
                }
            }
            else
            {
                diff.ColumnDiffs[colName] = ColumnDiff.Added(srcCol);
            }
        }

        // Check columns in target deleted from source
        foreach (var (colName, tgtCol) in target.Columns)
        {
            if (!source.Columns.ContainsKey(colName))
            {
                diff.ColumnDiffs[colName] = ColumnDiff.Deleted(tgtCol);
            }
        }

        // Check foreign keys
        var srcFkDict = source.ForeignKeys.ToDictionary(ForeignKeyDiff.GetKey, StringComparer.OrdinalIgnoreCase);
        var tgtFkDict = target.ForeignKeys.ToDictionary(ForeignKeyDiff.GetKey, StringComparer.OrdinalIgnoreCase);

        foreach (var (fkName, srcFk) in srcFkDict)
        {
            if (!tgtFkDict.ContainsKey(fkName))
            {
                diff.ForeignKeyDiffs.Add(ForeignKeyDiff.Added(srcFk));
            }
        }

        foreach (var (fkName, tgtFk) in tgtFkDict)
        {
            if (!srcFkDict.ContainsKey(fkName))
            {
                diff.ForeignKeyDiffs.Add(ForeignKeyDiff.Deleted(tgtFk));
            }
        }

        if (diff.HasChanges)
        {
            diff.DiffType = DiffType.Modified;
            return diff;
        }

        return null;
    }
}
