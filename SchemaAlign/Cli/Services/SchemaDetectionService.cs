using SchemaAlign.Models;
using SchemaAlign.Readers;
using SchemaAlign.Readers.CSharp;
using SchemaAlign.Readers.Mermaid;
using SchemaAlign.Readers.SqlServer;

namespace SchemaAlign.Cli.Services;

/// <summary>
/// Service for detecting schema formats and reading database schemas from files, directories, or connection strings.
/// </summary>
public class SchemaDetectionService
{
    /// <summary>
    /// Detects and returns an appropriate <see cref="ISchemaReader"/> for the given path, multi-path string, or connection string.
    /// </summary>
    /// <param name="pathOrConnectionString">The file path, directory, semicolon/comma-separated multi-path, or database connection string.</param>
    /// <returns>A schema reader instance capable of parsing the source.</returns>
    /// <exception cref="ArgumentException">Thrown when the input path or connection string is empty.</exception>
    /// <exception cref="NotSupportedException">Thrown when the format is unsupported or the required reader is unavailable.</exception>
    public virtual ISchemaReader DetectReader(string pathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(pathOrConnectionString))
            throw new ArgumentException("Path or connection string cannot be empty.", nameof(pathOrConnectionString));

        var trimmed = pathOrConnectionString.Trim();

        // Check for semicolon or comma-separated multi-paths (ignoring SQL connection strings)
        if (!IsConnectionString(trimmed) && (trimmed.Contains(';') || trimmed.Contains(',')))
        {
            var parts = trimmed.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1 && parts.All(p => Directory.Exists(p) || File.Exists(p) || p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                return new CSharpEntityReader();
            }
        }

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
        if (IsConnectionString(trimmed))
        {
            return new SqlServerSchemaReader();
        }

        // 4. SQL script file
        if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlScriptSchemaReader();
        }

        throw new NotSupportedException($"Schema format for '{pathOrConnectionString}' is not supported. Supported formats: .mmd, .mermaid, .cs, C# entity directory, .sql, or SQL connection strings.");
    }

    /// <summary>
    /// Detects the <see cref="TargetType"/> represented by the given path, multi-path string, or connection string.
    /// </summary>
    /// <param name="pathOrConnectionString">The file path, directory, semicolon/comma-separated multi-path, or database connection string.</param>
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

        if (IsConnectionString(trimmed))
        {
            return TargetType.SqlServerDatabase;
        }

        if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.SqlServerScript;
        }

        // Check for multi-path C# directories
        if (!IsConnectionString(trimmed) && (trimmed.Contains(';') || trimmed.Contains(',')))
        {
            var parts = trimmed.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
            {
                return TargetType.CSharp;
            }
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
    /// Reads and parses a database schema from the specified path, multi-path string, or connection string.
    /// </summary>
    /// <param name="pathOrConnectionString">The file path, directory, semicolon/comma-separated multi-path, or database connection string.</param>
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

        // 1. Semicolon or comma-separated multi-paths for C# directories/files
        if (!IsConnectionString(trimmed) && (trimmed.Contains(';') || trimmed.Contains(',')))
        {
            var parts = trimmed.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
            {
                var csFiles = new List<string>();
                foreach (var part in parts)
                {
                    if (File.Exists(part))
                    {
                        csFiles.Add(part);
                    }
                    else if (Directory.Exists(part))
                    {
                        var files = Directory.GetFiles(part, "*.cs", SearchOption.AllDirectories)
                            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                     && !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"));
                        csFiles.AddRange(files);
                    }
                    else
                    {
                        throw new DirectoryNotFoundException($"Directory or file not found: '{part}'");
                    }
                }

                return new CSharpEntityReader().ReadFiles(csFiles);
            }
        }

        // 2. Mermaid
        if (trimmed.EndsWith(".mmd", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".mermaid", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(trimmed))
                throw new FileNotFoundException($"Mermaid schema file not found: {trimmed}");

            var text = await File.ReadAllTextAsync(trimmed, cancellationToken);
            return new MermaidSchemaReader().Read(text);
        }

        // 3. C# Entity Single File
        if (trimmed.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return new CSharpEntityReader().ReadFile(trimmed);
        }

        // 4. C# Entity Directory
        if (Directory.Exists(trimmed))
        {
            return new CSharpEntityReader().ReadDirectory(trimmed);
        }

        // 5. SQL Server Connection String
        if (IsConnectionString(trimmed))
        {
            var reader = new SqlServerSchemaReader();
            return reader.Read(trimmed);
        }

        // 6. SQL Script file
        if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(trimmed))
                throw new FileNotFoundException($"SQL script file not found: {trimmed}");

            var reader = new SqlScriptSchemaReader();
            var text = await File.ReadAllTextAsync(trimmed, cancellationToken);
            return reader.Read(text);
        }

        throw new NotSupportedException($"Schema format for '{pathOrConnectionString}' is not supported. Supported formats: .mmd, .mermaid, .cs, C# entity directory, .sql, or SQL connection strings.");
    }

    private static bool IsConnectionString(string str)
    {
        return str.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase);
    }
}
