using SchemaAlign.Models;

namespace SchemaAlign.Diff;

public static class SchemaDiffCalculator
{
    public static SchemaDiff Calculate(DatabaseSchema source, DatabaseSchema target, SchemaDiffOptions? options = null)
    {
        options ??= SchemaDiffOptions.Incremental;

        var schemaDiff = new SchemaDiff
        {
            SourceSchema = source,
            TargetSchema = target
        };

        var allTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in source.Tables.Keys)
        {
            allTableNames.Add(name);
        }
        foreach (var name in target.Tables.Keys)
        {
            allTableNames.Add(name);
        }

        foreach (var tableName in allTableNames)
        {
            var sourceTable = source.FindTable(tableName);
            var targetTable = target.FindTable(tableName);

            if (sourceTable == null && targetTable != null)
            {
                // Added table
                var tableDiff = new TableDiff
                {
                    TableName = targetTable.Name,
                    Schema = targetTable.Schema,
                    Kind = DiffKind.Added,
                    Source = null,
                    Target = targetTable
                };

                foreach (var col in targetTable.Columns.Values)
                {
                    tableDiff.Columns.Add(new ColumnDiff
                    {
                        ColumnName = col.Name,
                        Kind = DiffKind.Added,
                        Source = null,
                        Target = col,
                        Changes = ChangeDetail.None
                    });
                }

                foreach (var fk in targetTable.ForeignKeys)
                {
                    tableDiff.ForeignKeys.Add(new ForeignKeyDiff
                    {
                        ConstraintName = fk.ConstraintName,
                        Kind = DiffKind.Added,
                        Source = null,
                        Target = fk,
                        CardinalityChanged = false
                    });
                }

                schemaDiff.Tables.Add(tableDiff);
            }
            else if (sourceTable != null && targetTable == null)
            {
                // Table in source but omitted from target
                if (!options.IgnoreOmittedTables)
                {
                    // Full snapshot mode: Deleted table
                    var tableDiff = new TableDiff
                    {
                        TableName = sourceTable.Name,
                        Schema = sourceTable.Schema,
                        Kind = DiffKind.Deleted,
                        Source = sourceTable,
                        Target = null
                    };

                    foreach (var col in sourceTable.Columns.Values)
                    {
                        tableDiff.Columns.Add(new ColumnDiff
                        {
                            ColumnName = col.Name,
                            Kind = DiffKind.Deleted,
                            Source = col,
                            Target = null,
                            Changes = ChangeDetail.None
                        });
                    }

                    foreach (var fk in sourceTable.ForeignKeys)
                    {
                        tableDiff.ForeignKeys.Add(new ForeignKeyDiff
                        {
                            ConstraintName = fk.ConstraintName,
                            Kind = DiffKind.Deleted,
                            Source = fk,
                            Target = null,
                            CardinalityChanged = false
                        });
                    }

                    schemaDiff.Tables.Add(tableDiff);
                }
                else
                {
                    // Incremental sprint mode: do nothing (omitted table is preserved)
                }
            }
            else if (sourceTable != null && targetTable != null)
            {
                // Compare existing tables
                var tableDiff = new TableDiff
                {
                    TableName = targetTable.Name,
                    Schema = targetTable.Schema,
                    Source = sourceTable,
                    Target = targetTable
                };

                // Compare columns (exhaustive within table definition)
                var allColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var colName in sourceTable.Columns.Keys)
                {
                    allColumnNames.Add(colName);
                }
                foreach (var colName in targetTable.Columns.Keys)
                {
                    allColumnNames.Add(colName);
                }

                foreach (var colName in allColumnNames)
                {
                    var sourceCol = sourceTable.FindColumn(colName);
                    var targetCol = targetTable.FindColumn(colName);

                    if (sourceCol == null && targetCol != null)
                    {
                        tableDiff.Columns.Add(new ColumnDiff
                        {
                            ColumnName = targetCol.Name,
                            Kind = DiffKind.Added,
                            Source = null,
                            Target = targetCol,
                            Changes = ChangeDetail.None
                        });
                    }
                    else if (sourceCol != null && targetCol == null)
                    {
                        tableDiff.Columns.Add(new ColumnDiff
                        {
                            ColumnName = sourceCol.Name,
                            Kind = DiffKind.Deleted,
                            Source = sourceCol,
                            Target = null,
                            Changes = ChangeDetail.None
                        });
                    }
                    else if (sourceCol != null && targetCol != null)
                    {
                        var changes = ChangeDetail.None;

                        if (sourceCol.Type != targetCol.Type)
                            changes |= ChangeDetail.TypeChanged;
                        if (sourceCol.Length != targetCol.Length)
                            changes |= ChangeDetail.LengthChanged;
                        if (sourceCol.Precision != targetCol.Precision)
                            changes |= ChangeDetail.PrecisionChanged;
                        if (sourceCol.Scale != targetCol.Scale)
                            changes |= ChangeDetail.ScaleChanged;
                        if (sourceCol.IsNullable != targetCol.IsNullable)
                            changes |= ChangeDetail.NullabilityChanged;
                        if (sourceCol.IsPrimaryKey != targetCol.IsPrimaryKey)
                            changes |= ChangeDetail.KeyStatusChanged;
                        if (sourceCol.IsIdentity != targetCol.IsIdentity)
                            changes |= ChangeDetail.IdentityChanged;
                        if (!string.Equals(sourceCol.DefaultValue, targetCol.DefaultValue, StringComparison.Ordinal))
                            changes |= ChangeDetail.DefaultValueChanged;
                        if (!string.Equals(sourceCol.Comment, targetCol.Comment, StringComparison.Ordinal))
                            changes |= ChangeDetail.CommentChanged;

                        var kind = changes != ChangeDetail.None ? DiffKind.Modified : DiffKind.Unchanged;

                        tableDiff.Columns.Add(new ColumnDiff
                        {
                            ColumnName = targetCol.Name,
                            Kind = kind,
                            Source = sourceCol,
                            Target = targetCol,
                            Changes = changes
                        });
                    }
                }

                // Compare foreign keys
                var matchedSourceFks = new HashSet<ForeignKeySchema>();

                foreach (var targetFk in targetTable.ForeignKeys)
                {
                    ForeignKeySchema? matchedSourceFk = null;

                    // Match by constraint name if available
                    if (!string.IsNullOrEmpty(targetFk.ConstraintName))
                    {
                        matchedSourceFk = sourceTable.ForeignKeys
                            .FirstOrDefault(sf => !matchedSourceFks.Contains(sf) &&
                                                  string.Equals(sf.ConstraintName, targetFk.ConstraintName, StringComparison.OrdinalIgnoreCase));
                    }

                    // Match by relation tuple if constraint name not matched
                    if (matchedSourceFk == null)
                    {
                        matchedSourceFk = sourceTable.ForeignKeys
                            .FirstOrDefault(sf => !matchedSourceFks.Contains(sf) &&
                                                  string.Equals(sf.PrincipalTable, targetFk.PrincipalTable, StringComparison.OrdinalIgnoreCase) &&
                                                  string.Equals(sf.PrincipalColumn, targetFk.PrincipalColumn, StringComparison.OrdinalIgnoreCase) &&
                                                  string.Equals(sf.DependentTable, targetFk.DependentTable, StringComparison.OrdinalIgnoreCase) &&
                                                  string.Equals(sf.DependentColumn, targetFk.DependentColumn, StringComparison.OrdinalIgnoreCase));
                    }

                    if (matchedSourceFk != null)
                    {
                        matchedSourceFks.Add(matchedSourceFk);

                        var cardinalityChanged = matchedSourceFk.Cardinality != targetFk.Cardinality;
                        var kind = cardinalityChanged ? DiffKind.Modified : DiffKind.Unchanged;

                        tableDiff.ForeignKeys.Add(new ForeignKeyDiff
                        {
                            ConstraintName = targetFk.ConstraintName ?? matchedSourceFk.ConstraintName,
                            Kind = kind,
                            Source = matchedSourceFk,
                            Target = targetFk,
                            CardinalityChanged = cardinalityChanged
                        });
                    }
                    else
                    {
                        tableDiff.ForeignKeys.Add(new ForeignKeyDiff
                        {
                            ConstraintName = targetFk.ConstraintName,
                            Kind = DiffKind.Added,
                            Source = null,
                            Target = targetFk,
                            CardinalityChanged = false
                        });
                    }
                }

                if (!options.IgnoreOmittedForeignKeys)
                {
                    foreach (var sourceFk in sourceTable.ForeignKeys)
                    {
                        if (!matchedSourceFks.Contains(sourceFk))
                        {
                            tableDiff.ForeignKeys.Add(new ForeignKeyDiff
                            {
                                ConstraintName = sourceFk.ConstraintName,
                                Kind = DiffKind.Deleted,
                                Source = sourceFk,
                                Target = null,
                                CardinalityChanged = false
                            });
                        }
                    }
                }

                // Determine table diff kind
                var hasColChanges = tableDiff.Columns.Any(c => c.Kind != DiffKind.Unchanged);
                var hasFkChanges = tableDiff.ForeignKeys.Any(fk => fk.Kind != DiffKind.Unchanged);

                tableDiff.Kind = (hasColChanges || hasFkChanges) ? DiffKind.Modified : DiffKind.Unchanged;

                schemaDiff.Tables.Add(tableDiff);
            }
        }

        return schemaDiff;
    }
}
