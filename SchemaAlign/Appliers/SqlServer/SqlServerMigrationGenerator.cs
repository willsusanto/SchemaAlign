using System.Text;
using System.Text.RegularExpressions;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Appliers.SqlServer;

/// <summary>
/// Generates idempotent T-SQL DDL migration scripts and batches from a <see cref="SchemaDiff"/>.
/// </summary>
public static class SqlServerMigrationGenerator
{
    /// <summary>
    /// Generates a formatted idempotent T-SQL migration script for the provided schema diff.
    /// </summary>
    public static string Generate(SchemaDiff diff, SqlServerApplierOptions? options = null)
    {
        options ??= new SqlServerApplierOptions();
        var sb = new StringBuilder();

        if (options.IncludeHeader)
        {
            sb.AppendLine("-- ============================================================================");
            sb.AppendLine("-- SchemaAlign SQL Server Migration Script");
            sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("-- ============================================================================");
            sb.AppendLine();
        }

        if (options.Transactional)
        {
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        // 1. Drop Foreign Keys (if modified/deleted)
        GenerateDropForeignKeys(diff, options, sb);

        // 2. Drop Tables (if AllowDrops is enabled)
        GenerateDropTables(diff, options, sb);

        // 3. Drop Columns (if AllowDrops is enabled)
        GenerateDropColumns(diff, options, sb);

        // 4. Create Added Tables
        GenerateCreateTables(diff, options, sb);

        // 5. Alter Modified Tables (Add / Alter Columns)
        GenerateAlterColumns(diff, options, sb);

        // 6. Add / Update Foreign Keys
        GenerateAddForeignKeys(diff, options, sb);

        if (options.Transactional)
        {
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine("GO");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits a generated T-SQL script into individual executable batches separated by GO statements.
    /// </summary>
    public static IReadOnlyList<string> SplitIntoBatches(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Array.Empty<string>();
        }

        var rawBatches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var batches = new List<string>();

        foreach (var batch in rawBatches)
        {
            var trimmed = batch.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                batches.Add(trimmed);
            }
        }

        return batches;
    }

    private static void GenerateDropForeignKeys(SchemaDiff diff, SqlServerApplierOptions options, StringBuilder sb)
    {
        foreach (var tableDiff in diff.Tables)
        {
            var schema = GetSchema(tableDiff.Schema, options);
            foreach (var fkDiff in tableDiff.DeletedForeignKeys)
            {
                var fk = fkDiff.Source;
                if (fk == null) continue;
                var constraintName = fk.ConstraintName ?? $"FK_{tableDiff.TableName}_{fk.PrincipalTable}_{fk.DependentColumn}";

                sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'{EscapeSql(constraintName)}')");
                sb.AppendLine("BEGIN");
                sb.AppendLine($"    ALTER TABLE [{schema}].[{tableDiff.TableName}] DROP CONSTRAINT [{constraintName}];");
                sb.AppendLine("END;");
                sb.AppendLine("GO");
                sb.AppendLine();
            }
        }
    }

    private static void GenerateDropTables(SchemaDiff diff, SqlServerApplierOptions options, StringBuilder sb)
    {
        if (!options.AllowDrops)
        {
            return;
        }

        foreach (var tableDiff in diff.DeletedTables)
        {
            var schema = GetSchema(tableDiff.Schema, options);
            sb.AppendLine($"IF OBJECT_ID(N'[{schema}].[{tableDiff.TableName}]', N'U') IS NOT NULL");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    DROP TABLE [{schema}].[{tableDiff.TableName}];");
            sb.AppendLine("END;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }
    }

    private static void GenerateDropColumns(SchemaDiff diff, SqlServerApplierOptions options, StringBuilder sb)
    {
        if (!options.AllowDrops)
        {
            return;
        }

        foreach (var tableDiff in diff.ModifiedTables)
        {
            var schema = GetSchema(tableDiff.Schema, options);
            foreach (var colDiff in tableDiff.DeletedColumns)
            {
                var colName = colDiff.ColumnName;
                sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schema}].[{tableDiff.TableName}]') AND name = N'{EscapeSql(colName)}')");
                sb.AppendLine("BEGIN");
                sb.AppendLine($"    ALTER TABLE [{schema}].[{tableDiff.TableName}] DROP COLUMN [{colName}];");
                sb.AppendLine("END;");
                sb.AppendLine("GO");
                sb.AppendLine();
            }
        }
    }

    private static void GenerateCreateTables(SchemaDiff diff, SqlServerApplierOptions options, StringBuilder sb)
    {
        foreach (var tableDiff in diff.AddedTables)
        {
            var table = tableDiff.Target ?? tableDiff.Source;
            if (table == null) continue;

            var schema = GetSchema(table.Schema, options);
            sb.AppendLine($"IF OBJECT_ID(N'[{schema}].[{table.Name}]', N'U') IS NULL");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    CREATE TABLE [{schema}].[{table.Name}] (");

            var colLines = new List<string>();
            foreach (var column in table.Columns.Values)
            {
                var colDef = BuildColumnDefinition(column);
                colLines.Add($"        {colDef}");
            }

            var pkColumns = table.PrimaryKeys.ToList();
            if (pkColumns.Count > 0)
            {
                var pkColsFormatted = string.Join(", ", pkColumns.Select(c => $"[{c}]"));
                colLines.Add($"        CONSTRAINT [PK_{table.Name}] PRIMARY KEY CLUSTERED ({pkColsFormatted})");
            }

            sb.AppendLine(string.Join("," + Environment.NewLine, colLines));
            sb.AppendLine("    );");

            // Extended properties for table
            if (!string.IsNullOrWhiteSpace(table.Comment))
            {
                sb.AppendLine($"    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{EscapeSql(table.Comment)}', @level0type=N'SCHEMA', @level0name=N'{schema}', @level1type=N'TABLE', @level1name=N'{table.Name}';");
            }

            // Extended properties for columns
            foreach (var column in table.Columns.Values)
            {
                if (!string.IsNullOrWhiteSpace(column.Comment))
                {
                    sb.AppendLine($"    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{EscapeSql(column.Comment)}', @level0type=N'SCHEMA', @level0name=N'{schema}', @level1type=N'TABLE', @level1name=N'{table.Name}', @level2type=N'COLUMN', @level2name=N'{column.Name}';");
                }
            }

            sb.AppendLine("END;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }
    }

    private static void GenerateAlterColumns(SchemaDiff diff, SqlServerApplierOptions options, StringBuilder sb)
    {
        foreach (var tableDiff in diff.ModifiedTables)
        {
            var schema = GetSchema(tableDiff.Schema, options);

            // Added columns
            foreach (var colDiff in tableDiff.AddedColumns)
            {
                var column = colDiff.Target;
                if (column == null) continue;

                var colDef = BuildColumnDefinition(column, tableDiff.TableName);
                sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{schema}].[{tableDiff.TableName}]') AND name = N'{EscapeSql(column.Name)}')");
                sb.AppendLine("BEGIN");
                sb.AppendLine($"    ALTER TABLE [{schema}].[{tableDiff.TableName}] ADD {colDef};");

                if (!string.IsNullOrWhiteSpace(column.Comment))
                {
                    sb.AppendLine($"    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{EscapeSql(column.Comment)}', @level0type=N'SCHEMA', @level0name=N'{schema}', @level1type=N'TABLE', @level1name=N'{tableDiff.TableName}', @level2type=N'COLUMN', @level2name=N'{column.Name}';");
                }

                sb.AppendLine("END;");
                sb.AppendLine("GO");
                sb.AppendLine();
            }

            // Modified columns
            foreach (var colDiff in tableDiff.ModifiedColumns)
            {
                var column = colDiff.Target ?? colDiff.Source;
                if (column == null) continue;

                var requiresAlterColumn = colDiff.Changes == ChangeDetail.None
                    ? (colDiff.Source != null && colDiff.Target != null &&
                       (colDiff.Source.Type != colDiff.Target.Type ||
                        colDiff.Source.Length != colDiff.Target.Length ||
                        colDiff.Source.Precision != colDiff.Target.Precision ||
                        colDiff.Source.Scale != colDiff.Target.Scale ||
                        colDiff.Source.IsNullable != colDiff.Target.IsNullable))
                    : (colDiff.Changes & (ChangeDetail.TypeChanged | ChangeDetail.LengthChanged | ChangeDetail.PrecisionChanged | ChangeDetail.ScaleChanged | ChangeDetail.NullabilityChanged)) != 0;

                if (requiresAlterColumn)
                {
                    var sqlType = TypeMapper.ToSqlServerType(column);
                    var nullability = column.IsPrimaryKey ? "NOT NULL" : (column.IsNullable ? "NULL" : "NOT NULL");

                    sb.AppendLine($"ALTER TABLE [{schema}].[{tableDiff.TableName}] ALTER COLUMN [{column.Name}] {sqlType} {nullability};");
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(column.Comment))
                {
                    sb.AppendLine($"IF EXISTS (SELECT 1 FROM sys.fn_listextendedproperty(N'MS_Description', N'SCHEMA', N'{schema}', N'TABLE', N'{tableDiff.TableName}', N'COLUMN', N'{column.Name}'))");
                    sb.AppendLine($"    EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'{EscapeSql(column.Comment)}', @level0type=N'SCHEMA', @level0name=N'{schema}', @level1type=N'TABLE', @level1name=N'{tableDiff.TableName}', @level2type=N'COLUMN', @level2name=N'{column.Name}';");
                    sb.AppendLine("ELSE");
                    sb.AppendLine($"    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'{EscapeSql(column.Comment)}', @level0type=N'SCHEMA', @level0name=N'{schema}', @level1type=N'TABLE', @level1name=N'{tableDiff.TableName}', @level2type=N'COLUMN', @level2name=N'{column.Name}';");
                    sb.AppendLine("GO");
                    sb.AppendLine();
                }
            }
        }
    }

    private static void GenerateAddForeignKeys(SchemaDiff diff, SqlServerApplierOptions options, StringBuilder sb)
    {
        // Collect FKs from AddedTables and ModifiedTables
        var allFksToAdd = new List<(string Schema, string TableName, ForeignKeySchema Fk)>();

        foreach (var tableDiff in diff.AddedTables)
        {
            var table = tableDiff.Target ?? tableDiff.Source;
            if (table == null) continue;
            var schema = GetSchema(table.Schema, options);
            foreach (var fk in table.ForeignKeys)
            {
                allFksToAdd.Add((schema, table.Name, fk));
            }
        }

        foreach (var tableDiff in diff.ModifiedTables)
        {
            var schema = GetSchema(tableDiff.Schema, options);
            foreach (var fkDiff in tableDiff.AddedForeignKeys)
            {
                var fk = fkDiff.Target;
                if (fk != null)
                {
                    allFksToAdd.Add((schema, tableDiff.TableName, fk));
                }
            }
        }

        foreach (var (schema, tableName, fk) in allFksToAdd)
        {
            var constraintName = fk.ConstraintName ?? $"FK_{tableName}_{fk.PrincipalTable}_{fk.DependentColumn}";
            var pSchema = GetPrincipalSchema(fk.PrincipalTable, diff, options);

            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'{EscapeSql(constraintName)}')");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT [{constraintName}] FOREIGN KEY ([{fk.DependentColumn}]) REFERENCES [{pSchema}].[{fk.PrincipalTable}] ([{fk.PrincipalColumn}]);");
            sb.AppendLine("END;");
            sb.AppendLine("GO");
            sb.AppendLine();
        }
    }

    private static string BuildColumnDefinition(ColumnSchema column, string? tableNameForConstraint = null)
    {
        var sqlType = TypeMapper.ToSqlServerType(column);
        var identity = column.IsIdentity ? " IDENTITY(1,1)" : "";
        var nullability = column.IsPrimaryKey ? " NOT NULL" : (column.IsNullable ? " NULL" : " NOT NULL");

        var defaultClause = "";
        if (!column.IsIdentity)
        {
            if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            {
                var defVal = column.DefaultValue.Trim();
                if (tableNameForConstraint != null)
                {
                    var constraintName = $"DF_{tableNameForConstraint}_{column.Name}";
                    if (defVal.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase))
                    {
                        defaultClause = $" CONSTRAINT [{constraintName}] {defVal}";
                    }
                    else
                    {
                        defaultClause = $" CONSTRAINT [{constraintName}] DEFAULT {defVal}";
                    }
                }
                else
                {
                    if (defVal.StartsWith("DEFAULT", StringComparison.OrdinalIgnoreCase))
                    {
                        defaultClause = $" {defVal}";
                    }
                    else
                    {
                        defaultClause = $" DEFAULT {defVal}";
                    }
                }
            }
            else if (!column.IsNullable && !column.IsPrimaryKey && tableNameForConstraint != null)
            {
                // When adding a NOT NULL column to an existing table without an explicit default,
                // SQL Server throws Msg 4901 on non-empty tables.
                // We provide a type-appropriate fallback default constraint to ensure safe migrations.
                var constraintName = $"DF_{tableNameForConstraint}_{column.Name}";
                var fallbackDefault = GetDefaultValueForType(column);
                defaultClause = $" CONSTRAINT [{constraintName}] DEFAULT {fallbackDefault}";
            }
        }

        return $"[{column.Name}] {sqlType}{identity}{nullability}{defaultClause}".TrimEnd();
    }

    private static string GetDefaultValueForType(ColumnSchema column)
    {
        return column.Type switch
        {
            StandardType.Boolean => "((0))",
            StandardType.Int or StandardType.BigInt or StandardType.SmallInt or StandardType.TinyInt => "((0))",
            StandardType.Decimal or StandardType.Double or StandardType.Float => "((0))",
            StandardType.Guid => "('00000000-0000-0000-0000-000000000000')",
            StandardType.DateTime or StandardType.Date or StandardType.DateTimeOffset => "('1900-01-01')",
            StandardType.Time => "('00:00:00')",
            StandardType.ByteArray => "(0x)",
            StandardType.Json => "('{}')",
            StandardType.String => "('')",
            _ => "('')"
        };
    }

    private static string GetSchema(string? schema, SqlServerApplierOptions options)
    {
        return string.IsNullOrWhiteSpace(schema) ? options.DefaultSchema : schema;
    }

    private static string GetPrincipalSchema(string principalTable, SchemaDiff diff, SqlServerApplierOptions options)
    {
        var foundTable = diff.FindTable(principalTable);
        if (foundTable != null)
        {
            var schema = foundTable.Target?.Schema ?? foundTable.Source?.Schema ?? foundTable.Schema;
            if (!string.IsNullOrWhiteSpace(schema))
            {
                return schema;
            }
        }

        var targetTable = diff.TargetSchema?.FindTable(principalTable);
        if (targetTable != null && !string.IsNullOrWhiteSpace(targetTable.Schema))
        {
            return targetTable.Schema;
        }

        var sourceTable = diff.SourceSchema?.FindTable(principalTable);
        if (sourceTable != null && !string.IsNullOrWhiteSpace(sourceTable.Schema))
        {
            return sourceTable.Schema;
        }

        return options.DefaultSchema;
    }

    private static string EscapeSql(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }
        return input.Replace("'", "''");
    }
}
