using SchemaAlign.Appliers.Diff;
using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Appliers.CSharp;

public class CSharpEntityApplier : ISchemaApplier
{
    public string Name => "CSharpEntityApplier";

    public string ApplyToSource(string sourceCode, TableDiff tableDiff, CSharpApplierOptions? options = null)
    {
        return CSharpEntityRewriter.Rewrite(sourceCode, tableDiff, options);
    }

    public string GenerateEntitySource(TableSchema table, CSharpApplierOptions? options = null)
    {
        return CSharpEntityGenerator.Generate(table, options);
    }

    public async Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(
        SchemaDiff diff,
        ApplierOptions options,
        CancellationToken cancellationToken = default)
    {
        var csOptions = options as CSharpApplierOptions ?? new CSharpApplierOptions
        {
            TargetDirectory = options.TargetDirectory
        };

        var previews = new List<FileDiffPreview>();
        var targetDir = csOptions.TargetDirectory;

        // Scan existing .cs files if directory exists
        var existingFiles = Directory.Exists(targetDir)
            ? Directory.GetFiles(targetDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList()
            : new List<string>();

        // 1. Handle Added Tables
        foreach (var addedTableDiff in diff.AddedTables)
        {
            var table = addedTableDiff.Target ?? addedTableDiff.Source;
            if (table == null) continue;

            var className = NamingHelper.ToEntityClassName(table.Name);
            var targetFilePath = Path.Combine(targetDir, $"{className}.cs");
            var relativePath = Path.GetRelativePath(targetDir, targetFilePath);

            var generatedContent = GenerateEntitySource(table, csOptions);
            var unifiedDiff = UnifiedDiffGenerator.GenerateDiff(null, generatedContent, relativePath);

            previews.Add(new FileDiffPreview
            {
                FilePath = targetFilePath,
                DiffKind = DiffKind.Added,
                OriginalContent = null,
                NewContent = generatedContent,
                UnifiedDiff = unifiedDiff
            });
        }

        // 2. Handle Modified Tables
        foreach (var modTableDiff in diff.ModifiedTables)
        {
            var matchingFile = FindMatchingFile(existingFiles, modTableDiff.TableName);
            if (matchingFile != null)
            {
                var originalContent = await File.ReadAllTextAsync(matchingFile, cancellationToken);
                var updatedContent = ApplyToSource(originalContent, modTableDiff, csOptions);

                if (originalContent != updatedContent)
                {
                    var relativePath = Path.GetRelativePath(targetDir, matchingFile);
                    var unifiedDiff = UnifiedDiffGenerator.GenerateDiff(originalContent, updatedContent, relativePath);

                    previews.Add(new FileDiffPreview
                    {
                        FilePath = matchingFile,
                        DiffKind = DiffKind.Modified,
                        OriginalContent = originalContent,
                        NewContent = updatedContent,
                        UnifiedDiff = unifiedDiff
                    });
                }
            }
        }

        // 3. Handle Deleted Tables
        if (csOptions.DeleteDroppedTables)
        {
            foreach (var delTableDiff in diff.DeletedTables)
            {
                var matchingFile = FindMatchingFile(existingFiles, delTableDiff.TableName);
                if (matchingFile != null)
                {
                    var originalContent = await File.ReadAllTextAsync(matchingFile, cancellationToken);
                    var relativePath = Path.GetRelativePath(targetDir, matchingFile);
                    var unifiedDiff = UnifiedDiffGenerator.GenerateDiff(originalContent, null, relativePath);

                    previews.Add(new FileDiffPreview
                    {
                        FilePath = matchingFile,
                        DiffKind = DiffKind.Deleted,
                        OriginalContent = originalContent,
                        NewContent = null,
                        UnifiedDiff = unifiedDiff
                    });
                }
            }
        }

        return previews;
    }

    public async Task<ApplierResult> ApplyAsync(
        SchemaDiff diff,
        ApplierOptions options,
        CancellationToken cancellationToken = default)
    {
        var csOptions = options as CSharpApplierOptions ?? new CSharpApplierOptions
        {
            TargetDirectory = options.TargetDirectory
        };

        var result = new ApplierResult();

        try
        {
            var previews = await PreviewAsync(diff, csOptions, cancellationToken);
            result.Previews.AddRange(previews);

            if (!Directory.Exists(csOptions.TargetDirectory))
            {
                Directory.CreateDirectory(csOptions.TargetDirectory);
            }

            foreach (var preview in previews)
            {
                var targetFile = preview.FilePath;
                var dir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (preview.DiffKind == DiffKind.Added)
                {
                    await File.WriteAllTextAsync(targetFile, preview.NewContent ?? string.Empty, cancellationToken);
                    result.CreatedFiles.Add(targetFile);
                }
                else if (preview.DiffKind == DiffKind.Modified)
                {
                    await File.WriteAllTextAsync(targetFile, preview.NewContent ?? string.Empty, cancellationToken);
                    result.ChangedFiles.Add(targetFile);
                }
                else if (preview.DiffKind == DiffKind.Deleted && File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                    result.DeletedFiles.Add(targetFile);
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private static string? FindMatchingFile(IEnumerable<string> files, string tableName)
    {
        var entityName = NamingHelper.ToEntityClassName(tableName);
        var pascalName = NamingHelper.ToPascalCase(tableName);

        foreach (var file in files)
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(fileNameWithoutExt, tableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileNameWithoutExt, entityName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileNameWithoutExt, pascalName, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }
}
