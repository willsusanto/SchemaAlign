using SchemaAlign.Models;
using SchemaAlign.Readers;
using SchemaAlign.Readers.CSharp;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Cli.Services;

/// <summary>
/// Service for detecting schema formats and reading database schemas from files, directories, or connection strings.
/// </summary>
public class SchemaDetectionService
{
    /// <summary>
    /// Detects and returns an appropriate <see cref="ISchemaReader"/> for the given path or connection string.
    /// </summary>
    /// <param name="pathOrConnectionString">The file path, directory, or database connection string.</param>
    /// <returns>A schema reader instance capable of parsing the source.</returns>
    /// <exception cref="ArgumentException">Thrown when the input path or connection string is empty.</exception>
    /// <exception cref="NotSupportedException">Thrown when the format is unsupported or the required reader is unavailable.</exception>
    public virtual ISchemaReader DetectReader(string pathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(pathOrConnectionString))
            throw new ArgumentException("Path or connection string cannot be empty.", nameof(pathOrConnectionString));

        var trimmed = pathOrConnectionString.Trim();

        // 1. Mermaid ER diagram file
        if (trimmed.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".mermaid", StringComparison.OrdinalIgnoreCase))
        {
            return new MermaidSchemaReader();
        }

        // 2. C# file or directory
        if (trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new CSharpEntityReader();
        }

        if (Directory.Exists(trimmed))
        {
            return new CSharpEntityReader();
        }

        // 3. SQL Server Connection string
        if (trimmed.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
        {
            // Dynamically locate SqlServerSchemaReader if compiled in another assembly or namespace
            var sqlServerType = Type.GetType("SchemaAlign.Readers.SqlServer.SqlServerSchemaReader, SchemaAlign");
            if (sqlServerType != null)
            {
                return (ISchemaReader)Activator.CreateInstance(sqlServerType)!;
            }
            throw new NotSupportedException($"SQL Server live connection reading is not yet available in this build: '{pathOrConnectionString}'.");
        }

        // 4. SQL script file
        if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            var sqlScriptType = Type.GetType("SchemaAlign.Readers.SqlServer.SqlScriptSchemaReader, SchemaAlign");
            if (sqlScriptType != null)
            {
                return (ISchemaReader)Activator.CreateInstance(sqlScriptType)!;
            }
            throw new NotSupportedException($"SQL script parsing is not yet available in this build: '{pathOrConnectionString}'.");
        }

        throw new NotSupportedException($"Schema format for '{pathOrConnectionString}' is not supported. Supported formats: .mmd, .mermaid, .cs, C# entity directory, .sql, or SQL connection strings.");
    }

    /// <summary>
    /// Detects the <see cref="TargetType"/> represented by the given path or connection string.
    /// </summary>
    /// <param name="pathOrConnectionString">The file path, directory, or database connection string.</param>
    /// <returns>The detected <see cref="TargetType"/>, or <see cref="TargetType.Unknown"/> if not recognized.</returns>
    public virtual TargetType DetectTargetType(string pathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(pathOrConnectionString))
            return TargetType.Unknown;

        var trimmed = pathOrConnectionString.Trim();

        if (trimmed.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".mermaid", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.Mermaid;
        }

        if (trimmed.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.SqlServerDatabase;
        }

        if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.SqlServerScript;
        }

        if (trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            Directory.Exists(trimmed) ||
            trimmed.Contains('/') || trimmed.Contains('\\'))
        {
            return TargetType.CSharp;
        }

        return TargetType.Unknown;
    }

    /// <summary>
    /// Reads and parses a database schema from the specified path or connection string.
    /// </summary>
    /// <param name="pathOrConnectionString">The file path, directory, or database connection string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="DatabaseSchema"/> representing the parsed schema.</returns>
    /// <exception cref="ArgumentException">Thrown when the input path or connection string is empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    /// <exception cref="NotSupportedException">Thrown when the format is unsupported or required reader is unavailable.</exception>
    public virtual async Task<DatabaseSchema> ReadSchemaAsync(string pathOrConnectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrConnectionString))
            throw new ArgumentException("Path or connection string cannot be empty.", nameof(pathOrConnectionString));

        var trimmed = pathOrConnectionString.Trim();

        // 1. Mermaid
        if (trimmed.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".mermaid", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(trimmed))
                throw new FileNotFoundException($"Mermaid schema file not found: {trimmed}");

            var text = await File.ReadAllTextAsync(trimmed, cancellationToken);
            return new MermaidSchemaReader().Read(text);
        }

        // 2. C# Entity Single File
        if (trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new CSharpEntityReader().ReadFile(trimmed);
        }

        // 3. C# Entity Directory
        if (Directory.Exists(trimmed))
        {
            return new CSharpEntityReader().ReadDirectory(trimmed);
        }

        // 4. SQL Server Connection String
        if (trimmed.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
        {
            var sqlServerType = Type.GetType("SchemaAlign.Readers.SqlServer.SqlServerSchemaReader, SchemaAlign");
            if (sqlServerType != null)
            {
                var reader = (ISchemaReader)Activator.CreateInstance(sqlServerType)!;
                return reader.Read(trimmed);
            }
            throw new NotSupportedException($"SQL Server live connection reading is not yet available in this build: '{pathOrConnectionString}'.");
        }

        // 5. SQL Script file
        if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(trimmed))
                throw new FileNotFoundException($"SQL script file not found: {trimmed}");

            var sqlScriptType = Type.GetType("SchemaAlign.Readers.SqlServer.SqlScriptSchemaReader, SchemaAlign");
            if (sqlScriptType != null)
            {
                var reader = (ISchemaReader)Activator.CreateInstance(sqlScriptType)!;
                var text = await File.ReadAllTextAsync(trimmed, cancellationToken);
                return reader.Read(text);
            }
            throw new NotSupportedException($"SQL script parsing is not yet available in this build: '{pathOrConnectionString}'.");
        }

        throw new NotSupportedException($"Schema format for '{pathOrConnectionString}' is not supported. Supported formats: .mmd, .mermaid, .cs, C# entity directory, .sql, or SQL connection strings.");
    }
}
