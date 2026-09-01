using SchemaAlign.Appliers.Diff;
using SchemaAlign.Diff;

namespace SchemaAlign.Appliers.SqlServer;

/// <summary>
/// Schema applier for SQL Server, supporting idempotent T-SQL DDL script generation, file diff previews, and direct live database execution.
/// </summary>
public class SqlServerMigrationApplier : ISchemaApplier
{
    private readonly ISqlMigrationExecutor _executor;

    /// <summary>
    /// Gets the unique applier name ("SqlServerMigrationApplier").
    /// </summary>
    public string Name => "SqlServerMigrationApplier";

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerMigrationApplier"/> class.
    /// </summary>
    /// <param name="executor">Optional SQL migration executor for live database execution.</param>
    public SqlServerMigrationApplier(ISqlMigrationExecutor? executor = null)
    {
        _executor = executor ?? new SqlMigrationExecutor();
    }

    /// <summary>
    /// Generates a formatted idempotent T-SQL migration script for the provided schema diff.
    /// </summary>
    public string GenerateMigrationScript(SchemaDiff diff, SqlServerApplierOptions? options = null)
    {
        return SqlServerMigrationGenerator.Generate(diff, options);
    }

    /// <summary>
    /// Generates file diff previews for the migration script or live database changes without modifying target files or databases.
    /// </summary>
    public async Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(
        SchemaDiff diff,
        ApplierOptions options,
        CancellationToken cancellationToken = default)
    {
        var sqlOptions = options as SqlServerApplierOptions ?? new SqlServerApplierOptions
        {
            TargetDirectory = options.TargetDirectory,
            AllowDrops = options.AllowDrops,
            DryRun = options.DryRun
        };

        var previews = new List<FileDiffPreview>();
        var scriptContent = GenerateMigrationScript(diff, sqlOptions);

        var connString = !string.IsNullOrWhiteSpace(sqlOptions.ConnectionString)
            ? sqlOptions.ConnectionString
            : (IsConnectionString(options.TargetDirectory) ? options.TargetDirectory : null);

        if (!string.IsNullOrWhiteSpace(connString))
        {
            var dbLabel = ExtractDatabaseName(connString);
            var displayPath = $"[SQL Server Live DB] {dbLabel}";
            var unifiedDiff = UnifiedDiffGenerator.GenerateDiff(null, scriptContent, displayPath);

            previews.Add(new FileDiffPreview
            {
                FilePath = displayPath,
                DiffKind = DiffKind.Modified,
                OriginalContent = null,
                NewContent = scriptContent,
                UnifiedDiff = unifiedDiff
            });

            return previews;
        }

        var targetFile = ResolveTargetFilePath(options.TargetDirectory, sqlOptions);
        var relativePath = Path.GetFileName(targetFile);

        if (File.Exists(targetFile))
        {
            var originalContent = await File.ReadAllTextAsync(targetFile, cancellationToken);
            var unifiedDiff = UnifiedDiffGenerator.GenerateDiff(originalContent, scriptContent, relativePath);

            previews.Add(new FileDiffPreview
            {
                FilePath = targetFile,
                DiffKind = DiffKind.Modified,
                OriginalContent = originalContent,
                NewContent = scriptContent,
                UnifiedDiff = unifiedDiff
            });
        }
        else
        {
            var unifiedDiff = UnifiedDiffGenerator.GenerateDiff(null, scriptContent, relativePath);

            previews.Add(new FileDiffPreview
            {
                FilePath = targetFile,
                DiffKind = DiffKind.Added,
                OriginalContent = null,
                NewContent = scriptContent,
                UnifiedDiff = unifiedDiff
            });
        }

        return previews;
    }

    /// <summary>
    /// Applies the schema diff by writing the T-SQL migration script to disk or executing it directly against a live SQL Server database.
    /// </summary>
    public async Task<ApplierResult> ApplyAsync(
        SchemaDiff diff,
        ApplierOptions options,
        CancellationToken cancellationToken = default)
    {
        var sqlOptions = options as SqlServerApplierOptions ?? new SqlServerApplierOptions
        {
            TargetDirectory = options.TargetDirectory,
            AllowDrops = options.AllowDrops,
            DryRun = options.DryRun
        };

        var result = new ApplierResult();

        try
        {
            var previews = await PreviewAsync(diff, sqlOptions, cancellationToken);
            result.Previews.AddRange(previews);

            if (sqlOptions.DryRun)
            {
                return result;
            }

            var connString = !string.IsNullOrWhiteSpace(sqlOptions.ConnectionString)
                ? sqlOptions.ConnectionString
                : (IsConnectionString(options.TargetDirectory) ? options.TargetDirectory : null);

            if (!string.IsNullOrWhiteSpace(connString))
            {
                var script = GenerateMigrationScript(diff, sqlOptions);
                var batches = SqlServerMigrationGenerator.SplitIntoBatches(script);

                await _executor.ExecuteBatchesAsync(connString, batches, sqlOptions.Transactional, cancellationToken);
                result.ChangedFiles.Add($"[SQL Server Live DB] {ExtractDatabaseName(connString)}");
                return result;
            }

            var targetFile = ResolveTargetFilePath(options.TargetDirectory, sqlOptions);
            var dir = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var isNew = !File.Exists(targetFile);
            var scriptContent = GenerateMigrationScript(diff, sqlOptions);

            await File.WriteAllTextAsync(targetFile, scriptContent, cancellationToken);

            if (isNew)
            {
                result.CreatedFiles.Add(targetFile);
            }
            else
            {
                result.ChangedFiles.Add(targetFile);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private static string ResolveTargetFilePath(string targetDirectoryOrFile, SqlServerApplierOptions options)
    {
        if (string.IsNullOrWhiteSpace(targetDirectoryOrFile))
        {
            return options.ScriptFileName;
        }

        if (targetDirectoryOrFile.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return targetDirectoryOrFile;
        }

        return Path.Combine(targetDirectoryOrFile, options.ScriptFileName);
    }

    private static bool IsConnectionString(string pathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(pathOrConnectionString))
            return false;

        return pathOrConnectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
               pathOrConnectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
               pathOrConnectionString.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            connectionString,
            @"(?:Database|Initial Catalog)\s*=\s*([^;]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value.Trim() : connectionString;
    }
}
