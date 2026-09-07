using SchemaAlign.Appliers;
using SchemaAlign.Cli.Commands;
using SchemaAlign.Cli.Rendering;
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

        var sourceSchema = new DatabaseSchema(); // Current base
        var targetSchema = new DatabaseSchema(); // Desired spec
        var newTable = new TableSchema { Name = "CustomerTable" };
        newTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Source = "Entities",
            Target = "schema.mmd",
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

        var sourceSchema = new DatabaseSchema(); // Current base with old table
        var oldTable = new TableSchema { Name = "OldAuditTable" };
        sourceSchema.AddTable(oldTable);

        var targetSchema = new DatabaseSchema(); // Desired spec with new table
        var newTable = new TableSchema { Name = "NewTable" };
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Source = "Entities",
            Target = "schema.mmd",
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

    [Fact]
    public async Task ExecuteSync_WithRealSqlServerApplier_GeneratesMigrationFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_SyncSqlTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var console = new TestConsole();
            var registry = new ApplierRegistry();

            var sourceSchema = new DatabaseSchema();
            var targetSchema = new DatabaseSchema();
            var newTable = new TableSchema { Name = "Orders" };
            newTable.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.BigInt, IsPrimaryKey = true, IsIdentity = true });
            targetSchema.AddTable(newTable);

            var sqlFile = Path.Combine(tempDir, "migration.sql");
            var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
            var options = new SyncCommandOptions
            {
                Source = sqlFile,
                Target = "schema.mmd",
                DryRun = false,
                Yes = true,
                Interactive = false
            };

            var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.SqlServerScript);

            exitCode.Should().Be(0);
            File.Exists(sqlFile).Should().BeTrue();
            var content = await File.ReadAllTextAsync(sqlFile);
            content.Should().Contain("CREATE TABLE [dbo].[Orders]");
            content.Should().Contain("[OrderId] BIGINT IDENTITY(1,1) NOT NULL");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void RenderPreviews_NullOriginalContent_RendersCleanContentWithoutLeadingPlusSigns()
    {
        var console = new TestConsole();
        var previews = new List<FileDiffPreview>
        {
            new()
            {
                FilePath = "[SQL Server Live DB] LibraryDB",
                DiffKind = DiffKind.Modified,
                OriginalContent = null,
                NewContent = "CREATE TABLE [dbo].[Books] (\n    [Id] INT NOT NULL\n);",
                UnifiedDiff = "@@ -0,0 +1,3 @@\n+CREATE TABLE [dbo].[Books] (\n+    [Id] INT NOT NULL\n+);"
            }
        };

        PreviewConsoleRenderer.RenderPreviews(previews, console);

        var output = console.Output;
        output.Should().Contain("CREATE TABLE [dbo].[Books]");
        output.Should().NotContain("+CREATE TABLE");
        output.Should().NotContain("+    [Id]");
    }

    [Fact]
    public void RenderPreviews_WithOriginalContent_RendersUnifiedDiffWithPlusAndMinus()
    {
        var console = new TestConsole();
        var previews = new List<FileDiffPreview>
        {
            new()
            {
                FilePath = "migration.sql",
                DiffKind = DiffKind.Modified,
                OriginalContent = "CREATE TABLE [dbo].[Books] ( [Id] INT );",
                NewContent = "CREATE TABLE [dbo].[Books] ( [Id] INT, [Title] NVARCHAR(100) );",
                UnifiedDiff = "--- migration.sql\n+++ migration.sql\n@@ -1 +1 @@\n-CREATE TABLE [dbo].[Books] ( [Id] INT );\n+CREATE TABLE [dbo].[Books] ( [Id] INT, [Title] NVARCHAR(100) );"
            }
        };

        PreviewConsoleRenderer.RenderPreviews(previews, console);

        var output = console.Output;
        output.Should().Contain("-CREATE TABLE");
        output.Should().Contain("+CREATE TABLE");
    }
}
