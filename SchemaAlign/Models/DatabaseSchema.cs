namespace SchemaAlign.Models;

public class DatabaseSchema
{
    public Dictionary<string, TableSchema> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddTable(TableSchema table)
    {
        Tables[table.Name] = table;
    }

    public TableSchema? FindTable(string name)
    {
        return Tables.TryGetValue(name, out var table) ? table : null;
    }

    public TableSchema GetOrCreateTable(string name, string schema = "dbo")
    {
        if (!Tables.TryGetValue(name, out var table))
        {
            table = new TableSchema { Name = name, Schema = schema };
            Tables[name] = table;
        }
        return table;
    }
}
