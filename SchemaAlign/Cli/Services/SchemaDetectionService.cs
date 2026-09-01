using SchemaAlign.Models;
using SchemaAlign.Readers;
using SchemaAlign.Readers.CSharp;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Cli.Services;

public class SchemaDetectionService
{
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
