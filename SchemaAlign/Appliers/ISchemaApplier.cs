using SchemaAlign.Diff;

namespace SchemaAlign.Appliers;

/// <summary>
/// Defines a contract for applying schema diffs to target codebases or schemas.
/// </summary>
public interface ISchemaApplier
{
    /// <summary>
    /// Gets the unique name of this schema applier.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Generates unified file diff previews for all changes required by the schema diff without modifying files on disk or databases.
    /// </summary>
    Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the schema diff to target files on disk or databases, writing code or executing migrations.
    /// </summary>
    Task<ApplierResult> ApplyAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default);
}
