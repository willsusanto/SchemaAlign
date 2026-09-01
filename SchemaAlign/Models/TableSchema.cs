namespace SchemaAlign.Models;

public class TableSchema
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public Dictionary<string, ColumnSchema> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
    public string? Comment { get; set; }

    public IEnumerable<string> PrimaryKeys =>
        Columns.Values.Where(c => c.IsPrimaryKey).Select(c => c.Name);

    public void AddColumn(ColumnSchema column)
    {
        Columns[column.Name] = column;
    }

    public ColumnSchema? FindColumn(string name)
    {
        return Columns.TryGetValue(name, out var column) ? column : null;
    }

    public void AddForeignKey(ForeignKeySchema foreignKey)
    {
        ForeignKeys.Add(foreignKey);
    }
}
