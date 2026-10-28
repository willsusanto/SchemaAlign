namespace SchemaAlign.Cli.Services;

/// <summary>Validates local wizard inputs before asking subsequent questions.</summary>
public static class SchemaInputValidator
{
    /// <summary>Returns absolute local paths, or an unchanged database connection string.</summary>
    public static string ValidateAndNormalize(string input)
    {
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Enter a schema path or SQL Server connection string.");

        if (SchemaDetectionService.IsConnectionString(value))
            return value;

        var parts = value.Split(new[] { ';', ',' }, StringSplitOptions.TrimEntries);
        var paths = parts.Select(part => ValidatePath(part, parts.Length > 1)).ToArray();
        return string.Join(";", paths);
    }

    private static string ValidatePath(string input, bool multiple)
    {
        var path = input.Trim();
        if (path.Length >= 2 && ((path[0] == '"' && path[^1] == '"') ||
                                 (path[0] == '\'' && path[^1] == '\'')))
            path = path[1..^1].Trim();

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Enter a path for every schema input.");

        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("Invalid schema path. Enter a valid file or directory path.");
        }

        if (Directory.Exists(path))
            return path;

        var extension = Path.GetExtension(path);
        var isCSharp = extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);
        var isSchemaFile = isCSharp || extension.Equals(".mmd", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".mermaid", StringComparison.OrdinalIgnoreCase) ||
                           extension.Equals(".sql", StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(path))
            throw new ArgumentException($"{(isSchemaFile || extension.Length > 0 ? "File" : "Directory")} not found: {path}");

        if (multiple && !isCSharp)
            throw new ArgumentException("Multiple paths support only C# files and entity directories.");

        if (!isSchemaFile)
            throw new ArgumentException($"Unsupported schema file: {path}. Use .mmd, .mermaid, .cs, or .sql.");

        return path;
    }
}
