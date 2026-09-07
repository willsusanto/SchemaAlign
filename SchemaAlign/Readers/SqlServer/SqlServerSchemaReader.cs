using SchemaAlign.Models;
using SchemaAlign.Models.TypeMapping;

namespace SchemaAlign.Readers.SqlServer;

public class SqlServerSchemaReader : ISchemaReader
{
    private readonly ISqlCatalogExecutor _executor;

    public SqlServerSchemaReader() : this(new SqlCatalogExecutor())
    {
    }

    public SqlServerSchemaReader(ISqlCatalogExecutor? executor = null)
    {
        _executor = executor ?? new SqlCatalogExecutor();
    }

    public DatabaseSchema Read(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new DatabaseSchema();
        }

        var schema = new DatabaseSchema();
        var tableRows = _executor.GetTables(content);
        if (tableRows.Count == 0)
        {
            return schema;
        }

        var tableMap = new Dictionary<int, TableSchema>();
        foreach (var row in tableRows)
        {
            var table = schema.GetOrCreateTable(row.TableName, row.Schema);
            tableMap[row.ObjectId] = table;
        }

        var columnRows = _executor.GetColumns(content);
        var columnMap = new Dictionary<(int ObjectId, int ColumnId), ColumnSchema>();
        foreach (var col in columnRows)
        {
            if (!tableMap.TryGetValue(col.ObjectId, out var table))
            {
                continue;
            }

            int? inLength = null;
            int? inPrecision = null;
            int? inScale = null;

            var lowerType = col.TypeName.ToLowerInvariant();
            if (lowerType is "nvarchar" or "nchar")
            {
                inLength = col.MaxLength == -1 ? -1 : col.MaxLength / 2;
            }
            else if (lowerType is "varchar" or "char" or "varbinary" or "binary")
            {
                inLength = col.MaxLength;
            }
            else if (lowerType is "decimal" or "numeric")
            {
                inPrecision = col.Precision;
                inScale = col.Scale;
            }
            else if (lowerType is "datetime2" or "datetimeoffset" or "time")
            {
                inPrecision = col.Scale;
            }

            var standardType = TypeMapper.FromSqlServerType(
                col.TypeName, inLength, inPrecision, inScale,
                out var mappedLength, out var mappedPrecision, out var mappedScale);

            var column = new ColumnSchema
            {
                Name = col.ColumnName,
                Type = standardType,
                RawType = col.TypeName,
                Length = mappedLength,
                Precision = mappedPrecision,
                Scale = mappedScale,
                IsNullable = col.IsNullable,
                IsIdentity = col.IsIdentity,
                DefaultValue = col.DefaultDefinition
            };

            table.AddColumn(column);
            columnMap[(col.ObjectId, col.ColumnId)] = column;
        }

        var keyRows = _executor.GetKeyConstraints(content);
        foreach (var key in keyRows)
        {
            if (tableMap.TryGetValue(key.ObjectId, out var table))
            {
                var column = table.FindColumn(key.ColumnName);
                if (column != null && key.ConstraintType.Equals("PK", StringComparison.OrdinalIgnoreCase))
                {
                    column.IsPrimaryKey = true;
                }
            }
        }

        var fkRows = _executor.GetForeignKeys(content);
        foreach (var fkRow in fkRows)
        {
            var dependentTable = schema.FindTable(fkRow.ParentTable);
            if (dependentTable != null)
            {
                var fk = new ForeignKeySchema
                {
                    ConstraintName = fkRow.ConstraintName,
                    DependentTable = fkRow.ParentTable,
                    DependentColumn = fkRow.ParentColumn,
                    PrincipalTable = fkRow.ReferencedTable,
                    PrincipalColumn = fkRow.ReferencedColumn,
                    Cardinality = ForeignKeyCardinality.ManyToOne
                };
                dependentTable.AddForeignKey(fk);
            }
        }

        var propRows = _executor.GetExtendedProperties(content);
        foreach (var prop in propRows)
        {
            if (tableMap.TryGetValue(prop.MajorId, out var table))
            {
                if (prop.MinorId == 0)
                {
                    table.Comment = prop.Value;
                }
                else if (columnMap.TryGetValue((prop.MajorId, prop.MinorId), out var column))
                {
                    column.Comment = prop.Value;
                }
            }
        }

        return schema;
    }
}
