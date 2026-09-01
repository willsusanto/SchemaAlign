using SchemaAlign.Appliers;
using SchemaAlign.Cli.Commands;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class SyncWorkflowTests
{
    private class MockApplier : ISchemaApplier
    {
        public string Name => "MockApplier";
        public bool ApplyCalled { get; private set; }
        public bool PreviewCalled { get; private set; }
        public SchemaDiff? LastDiffApplied { get; private set; }

        public Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
        {
            PreviewCalled = true;
            var previews = new List<FileDiffPreview>
            {
                new()
                {
                    FilePath = "TestEntity.cs",
                    DiffKind = DiffKind.Added,
                    UnifiedDiff = "+ public class TestEntity { }"
                }
            };
            return Task.FromResult<IReadOnlyList<FileDiffPreview>>(previews);
        }

        public Task<ApplierResult> ApplyAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
        {
            ApplyCalled = true;
            LastDiffApplied = diff;
            return Task.FromResult(new ApplierResult
            {
                Success = true,
                CreatedFiles = new List<string> { "TestEntity.cs" }
            });
        }
    }

    [Fact]
    public async Task ExecuteSync_DryRun_CallsPreviewButNotApply()
    {
        var console = new TestConsole();
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);

        var targetSchema = new DatabaseSchema();
        var sourceSchema = new DatabaseSchema();
        var newTable = new TableSchema { Name = "CustomerTable" };
        newTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        sourceSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Source = "schema.mmd",
            Target = "Entities",
            DryRun = true,
            Yes = true,
            Interactive = false
        };

        var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);

        exitCode.Should().Be(0);
        mockApplier.PreviewCalled.Should().BeTrue();
        mockApplier.ApplyCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteSync_NonInteractive_WithoutAllowDrops_FiltersDeletionsBeforeApply()
    {
        var console = new TestConsole();
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);

        var targetSchema = new DatabaseSchema();
        var oldTable = new TableSchema { Name = "OldAuditTable" };
        targetSchema.AddTable(oldTable);

        var sourceSchema = new DatabaseSchema();
        var newTable = new TableSchema { Name = "NewTable" };
        sourceSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Source = "schema.mmd",
            Target = "Entities",
            Mode = "snapshot",
            AllowDrop = false,
            DryRun = false,
            Yes = true,
            Interactive = false
        };

        var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);

        exitCode.Should().Be(0);
        mockApplier.ApplyCalled.Should().BeTrue();
        mockApplier.LastDiffApplied.Should().NotBeNull();
        mockApplier.LastDiffApplied!.DeletedTables.Should().BeEmpty();
        mockApplier.LastDiffApplied!.AddedTables.Should().ContainSingle(t => t.TableName == "NewTable");
    }
}
