using System.Data;
using Microsoft.Data.SqlClient;

namespace SchemaAlign.Readers.SqlServer;

public record TableCatalogRow(int ObjectId, string Schema, string TableName);

public record ColumnCatalogRow(
    int ObjectId,
    int ColumnId,
    string ColumnName,
    string TypeName,
    int MaxLength,
    byte Precision,
    byte Scale,
    bool IsNullable,
    bool IsIdentity,
    string? DefaultDefinition);

public record KeyConstraintCatalogRow(int ObjectId, string ColumnName, string ConstraintType);

public record ForeignKeyCatalogRow(
    string ConstraintName,
    string ParentSchema,
    string ParentTable,
    string ParentColumn,
    string ReferencedSchema,
    string ReferencedTable,
    string ReferencedColumn);

public record ExtendedPropertyCatalogRow(int MajorId, int MinorId, byte Class, string Name, string? Value);

public interface ISqlCatalogExecutor
{
    IReadOnlyList<TableCatalogRow> GetTables(string connectionString);
    IReadOnlyList<ColumnCatalogRow> GetColumns(string connectionString);
    IReadOnlyList<KeyConstraintCatalogRow> GetKeyConstraints(string connectionString);
    IReadOnlyList<ForeignKeyCatalogRow> GetForeignKeys(string connectionString);
    IReadOnlyList<ExtendedPropertyCatalogRow> GetExtendedProperties(string connectionString);
}

public class SqlCatalogExecutor : ISqlCatalogExecutor
{
    public IReadOnlyList<TableCatalogRow> GetTables(string connectionString)
    {
        const string sql = @"
SELECT t.object_id, s.name AS schema_name, t.name AS table_name
FROM sys.tables t
INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name;";

        return Query(connectionString, sql, reader => new TableCatalogRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2)
        ));
    }

    public IReadOnlyList<ColumnCatalogRow> GetColumns(string connectionString)
    {
        const string sql = @"
SELECT c.object_id, c.column_id, c.name AS column_name,
       tp.name AS type_name, c.max_length, c.precision, c.scale,
       c.is_nullable, c.is_identity, dc.definition AS default_definition
FROM sys.columns c
INNER JOIN sys.tables t ON c.object_id = t.object_id
INNER JOIN sys.types tp ON c.user_type_id = tp.user_type_id
LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
WHERE t.is_ms_shipped = 0
ORDER BY c.object_id, c.column_id;";

        return Query(connectionString, sql, reader => new ColumnCatalogRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt16(4),
            reader.GetByte(5),
            reader.GetByte(6),
            reader.GetBoolean(7),
            reader.GetBoolean(8),
            reader.IsDBNull(9) ? null : reader.GetString(9)
        ));
    }

    public IReadOnlyList<KeyConstraintCatalogRow> GetKeyConstraints(string connectionString)
    {
        const string sql = @"
SELECT kc.parent_object_id AS object_id, c.name AS column_name, kc.type AS constraint_type
FROM sys.key_constraints kc
INNER JOIN sys.indexes i ON kc.parent_object_id = i.object_id AND kc.unique_index_id = i.index_id
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE kc.type = 'PK'
ORDER BY kc.parent_object_id, ic.key_ordinal;";

        return Query(connectionString, sql, reader => new KeyConstraintCatalogRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2).Trim()
        ));
    }

    public IReadOnlyList<ForeignKeyCatalogRow> GetForeignKeys(string connectionString)
    {
        const string sql = @"
SELECT fk.name AS constraint_name,
       ps.name AS parent_schema, pt.name AS parent_table, pc.name AS parent_column,
       rs.name AS referenced_schema, rt.name AS referenced_table, rc.name AS referenced_column
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
INNER JOIN sys.tables pt ON fkc.parent_object_id = pt.object_id
INNER JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
INNER JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
INNER JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
INNER JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
INNER JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
ORDER BY fk.name, fkc.constraint_column_id;";

        return Query(connectionString, sql, reader => new ForeignKeyCatalogRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6)
        ));
    }

    public IReadOnlyList<ExtendedPropertyCatalogRow> GetExtendedProperties(string connectionString)
    {
        const string sql = @"
SELECT ep.major_id, ep.minor_id, ep.class, ep.name, CAST(ep.value AS NVARCHAR(MAX)) AS value
FROM sys.extended_properties ep
INNER JOIN sys.tables t ON ep.major_id = t.object_id
WHERE ep.class = 1 AND ep.name = 'MS_Description'
ORDER BY ep.major_id, ep.minor_id;";

        return Query(connectionString, sql, reader => new ExtendedPropertyCatalogRow(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetByte(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4)
        ));
    }

    private static List<T> Query<T>(string connectionString, string sql, Func<IDataRecord, T> map)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection);
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            results.Add(map(reader));
        }
        return results;
    }
}
