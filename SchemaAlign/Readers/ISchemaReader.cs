using SchemaAlign.Models;

namespace SchemaAlign.Readers;

/// <summary>
/// Defines the contract for parsing database schemas from various source formats.
/// </summary>
public interface ISchemaReader
{
    /// <summary>
    /// Reads and parses a database schema from string content.
    /// </summary>
    /// <param name="content">The schema content to parse.</param>
    /// <returns>A <see cref="DatabaseSchema"/> representing the parsed schema.</returns>
    DatabaseSchema Read(string content);
}
