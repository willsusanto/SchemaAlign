using SchemaAlign.Diff;

namespace SchemaAlign.Appliers;

public interface ISchemaApplier
{
    string Name { get; }

    Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default);

    Task<ApplierResult> ApplyAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default);
}
