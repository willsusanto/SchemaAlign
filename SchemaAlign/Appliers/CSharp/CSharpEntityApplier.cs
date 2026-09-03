using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaAlign.Appliers.Diff;
using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Appliers.CSharp;

/// <summary>
/// Schema applier for C# EF Core entities, supporting AST-based in-place updates, entity generation, and file diff previews.
/// </summary>
public class CSharpEntityApplier : ISchemaApplier
{
    /// <summary>
    /// Gets the applier name ("CSharpEntityApplier").
    /// </summary>
    public string Name => "CSharpEntityApplier";

    /// <summary>
    /// Applies a table diff to existing C# entity source code string in-place.
    /// </summary>
    public string ApplyToSource(string sourceCode, TableDiff tableDiff, CSharpApplierOptions? options = null)
    {
        return CSharpEntityRewriter.Rewrite(sourceCode, tableDiff, options);
    }

    /// <summary>
    /// Generates full C# entity source code for a table schema.
    /// </summary>
    public string GenerateEntitySource(TableSchema table, CSharpApplierOptions? options = null)
    {
        return CSharpEntityGenerator.Generate(table, options);
    }

    /// <summary>
    /// Generates file diff previews for all added, modified, or deleted entity files.
    /// </summary>
    public async Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(
        SchemaDiff diff,
        ApplierOptions options,
        CancellationToken cancellationToken = default)
    {
        var csOptions = options as CSharpApplierOptions ?? new CSharpApplierOptions
        {
            TargetDirectory = options.TargetDirectory,
            AllowDrops = options.AllowDrops,
            DryRun = options.DryRun
        };

        var previews = new List<FileDiffPreview>();
        var targetDir = csOptions.TargetDirectory;

        // Scan existing .cs files and resolve namespaces across target and source directories
        PrepareOptionsAndScan(csOptions, targetDir, out var existingFiles);

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

    /// <summary>
    /// Applies the schema diff directly to C# entity files on disk.
    /// </summary>
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

    private static void PrepareOptionsAndScan(CSharpApplierOptions csOptions, string targetDir, out List<string> existingFiles)
    {
        existingFiles = Directory.Exists(targetDir)
            ? Directory.GetFiles(targetDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToList()
            : new List<string>();

        var allDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
        {
            allDirs.Add(targetDir);
        }
        foreach (var sDir in csOptions.SourceDirectories)
        {
            if (!string.IsNullOrWhiteSpace(sDir) && Directory.Exists(sDir))
            {
                allDirs.Add(sDir);
            }
        }

        var allFiles = allDirs
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var targetDirNamespaces = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var classHierarchy = new Dictionary<string, (List<string> Props, List<string> BaseTypes)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in allFiles)
        {
            try
            {
                var content = File.ReadAllText(file);
                var syntaxTree = CSharpSyntaxTree.ParseText(content);
                var root = syntaxTree.GetCompilationUnitRoot();

                string? fileNs = null;
                var nsDecl = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
                if (nsDecl != null)
                {
                    fileNs = nsDecl.Name.ToString();
                }

                if (!string.IsNullOrWhiteSpace(fileNs) && existingFiles.Contains(file))
                {
                    targetDirNamespaces[fileNs] = targetDirNamespaces.GetValueOrDefault(fileNs, 0) + 1;
                }

                foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    var clsName = cls.Identifier.Text;
                    if (!string.IsNullOrWhiteSpace(fileNs) && !csOptions.EntityNamespaces.ContainsKey(clsName))
                    {
                        csOptions.EntityNamespaces[clsName] = fileNs;
                    }

                    var props = cls.Members.OfType<PropertyDeclarationSyntax>().Select(p => p.Identifier.Text).ToList();
                    var bases = cls.BaseList?.Types.Select(t => t.Type.ToString().Trim()).ToList() ?? new List<string>();
                    classHierarchy[clsName] = (props, bases);
                }
            }
            catch
            {
                // Ignore read or parse issues for individual files
            }
        }

        // If BaseClass is specified, automatically discover and omit all properties in its inheritance chain
        if (!string.IsNullOrWhiteSpace(csOptions.BaseClass))
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(csOptions.BaseClass.Trim());

            while (queue.Count > 0)
            {
                var currentClass = queue.Dequeue();
                if (!visited.Add(currentClass)) continue;

                if (classHierarchy.TryGetValue(currentClass, out var info))
                {
                    foreach (var prop in info.Props)
                    {
                        csOptions.OmitInheritedColumns.Add(prop);
                    }
                    foreach (var parentBase in info.BaseTypes)
                    {
                        queue.Enqueue(parentBase);
                    }
                }
            }
        }

        if (csOptions.AutoDetectNamespace &&
            (string.IsNullOrWhiteSpace(csOptions.DefaultNamespace) || csOptions.DefaultNamespace == "Entities") &&
            targetDirNamespaces.Count > 0)
        {
            var dominantNamespace = targetDirNamespaces.OrderByDescending(kvp => kvp.Value).First().Key;
            csOptions.DefaultNamespace = dominantNamespace;
        }
    }
}
