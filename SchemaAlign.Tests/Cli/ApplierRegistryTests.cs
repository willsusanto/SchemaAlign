using SchemaAlign.Appliers;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;

namespace SchemaAlign.Tests.Cli;

public class ApplierRegistryTests
{
    private class DummyApplier : ISchemaApplier
    {
        public string Name => "DummyApplier";
        public Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FileDiffPreview>>(Array.Empty<FileDiffPreview>());

        public Task<ApplierResult> ApplyAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new ApplierResult());
    }

    [Fact]
    public void Resolve_RegisteredCustomApplier_ReturnsInstance()
    {
        var registry = new ApplierRegistry();
        var dummy = new DummyApplier();
        registry.Register(TargetType.CSharp, dummy);

        var resolved = registry.Resolve(TargetType.CSharp);

        resolved.Should().BeSameAs(dummy);
    }

    [Fact]
    public void Resolve_UnregisteredTargetType_ThrowsNotSupportedException()
    {
        var registry = new ApplierRegistry();

        var act = () => registry.Resolve(TargetType.Unknown);

        act.Should().Throw<NotSupportedException>();
    }
}
