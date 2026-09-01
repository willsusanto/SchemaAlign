using SchemaAlign.Diff;

namespace SchemaAlign.Cli.Rendering;

public static class DiffFilter
{
    public static bool HasDestructiveChanges(SchemaDiff diff)
    {
        if (diff.DeletedTables.Any())
            return true;

        foreach (var table in diff.Tables)
        {
            if (table.DeletedColumns.Any() || table.DeletedForeignKeys.Any())
                return true;
        }

        return false;
    }

    public static SchemaDiff Filter(SchemaDiff diff, bool allowDrops)
    {
        if (allowDrops)
            return diff;

        var filtered = new SchemaDiff
        {
            SourceSchema = diff.SourceSchema,
            TargetSchema = diff.TargetSchema
        };

        foreach (var table in diff.Tables)
        {
            if (table.Kind == DiffKind.Deleted)
            {
                // Drop table excluded
                continue;
            }

            var newTableDiff = new TableDiff
            {
                TableName = table.TableName,
                Schema = table.Schema,
                Kind = table.Kind,
                Source = table.Source,
                Target = table.Target
            };

            foreach (var col in table.Columns)
            {
                if (col.Kind == DiffKind.Deleted)
                    continue;

                newTableDiff.Columns.Add(col);
            }

            foreach (var fk in table.ForeignKeys)
            {
                if (fk.Kind == DiffKind.Deleted)
                    continue;

                newTableDiff.ForeignKeys.Add(fk);
            }

            // Re-evaluate table kind if all column/fk changes were drops
            if (newTableDiff.Kind == DiffKind.Modified)
            {
                var hasColChanges = newTableDiff.Columns.Any(c => c.Kind != DiffKind.Unchanged);
                var hasFkChanges = newTableDiff.ForeignKeys.Any(f => f.Kind != DiffKind.Unchanged);
                newTableDiff.Kind = (hasColChanges || hasFkChanges) ? DiffKind.Modified : DiffKind.Unchanged;
            }

            filtered.Tables.Add(newTableDiff);
        }

        return filtered;
    }
}
