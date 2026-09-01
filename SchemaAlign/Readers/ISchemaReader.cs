using SchemaAlign.Models;

namespace SchemaAlign.Readers;

public interface ISchemaReader
{
    DatabaseSchema Read(string content);
}
