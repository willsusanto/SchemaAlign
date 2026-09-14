using SchemaAlign.Appliers;
using SchemaAlign.Appliers.CSharp;
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
        public ApplierOptions? LastOptionsUsed { get; private set; }

        public Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
        {
            PreviewCalled = true;
            LastOptionsUsed = options;
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
        var newTable = new TableSchema { Name = "tbl_masked_customer" };
        newTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Current = "Entities",
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
        var oldTable = new TableSchema { Name = "tbl_masked_audit" };
        sourceSchema.AddTable(oldTable);

        var targetSchema = new DatabaseSchema(); // Desired spec with new table
        var newTable = new TableSchema { Name = "tbl_masked_new" };
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Current = "Entities",
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
        mockApplier.LastDiffApplied!.AddedTables.Should().ContainSingle(t => t.TableName == "tbl_masked_new");
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
            var newTable = new TableSchema { Name = "tbl_masked_orders" };
            newTable.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.BigInt, IsPrimaryKey = true, IsIdentity = true });
            targetSchema.AddTable(newTable);

            var sqlFile = Path.Combine(tempDir, "migration.sql");
            var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
            var options = new SyncCommandOptions
            {
                Current = sqlFile,
                Target = "schema.mmd",
                DryRun = false,
                Yes = true,
                Interactive = false
            };

            var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.SqlServerScript);

            exitCode.Should().Be(0);
            File.Exists(sqlFile).Should().BeTrue();
            var content = await File.ReadAllTextAsync(sqlFile);
            content.Should().Contain("CREATE TABLE [dbo].[tbl_masked_orders]");
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

    [Fact]
    public async Task ExecuteSync_SqlServerDatabase_WithOutputFile_ExportsScriptFileDirectly()
    {
        var console = new TestConsole();
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.SqlServerDatabase, mockApplier);

        var currentSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();
        var table = new TableSchema { Name = "tbl_masked_sync_out" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        targetSchema.AddTable(table);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Current = "Server=sql;Database=TestDb;",
            Target = "schema.mmd",
            OutputFile = "custom_out.sql",
            Yes = true,
            Interactive = false
        };

        var exitCode = await handler.ExecuteAsync(currentSchema, targetSchema, options, TargetType.SqlServerDatabase);

        exitCode.Should().Be(0);
        mockApplier.ApplyCalled.Should().BeTrue();
        console.Output.Should().Contain("Successfully exported SQL Server migration script");
    }

    [Fact]
    public async Task ExecuteSync_WithCSharpConventions_PassesBaseClassAndAttributesToApplierOptions()
    {
        var console = new TestConsole();
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);

        var sourceSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();
        var newTable = new TableSchema { Name = "MockAsset" };
        newTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Current = "Entities",
            Target = "schema.mmd",
            BaseClass = "MockTrackedBase",
            DryRun = true,
            Yes = true,
            Interactive = false
        };
        options.ClassAttributes.Add("[DatabaseContext(\"MockStore\")]");
        options.Usings.Add("MockOrg.Framework.Patterns");
        options.OmitInheritedColumns.Add("AuditStamp");

        var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);

        exitCode.Should().Be(0);
        mockApplier.LastOptionsUsed.Should().NotBeNull();
        mockApplier.LastOptionsUsed.Should().BeOfType<CSharpApplierOptions>();

        var csOpts = (CSharpApplierOptions)mockApplier.LastOptionsUsed!;
        csOpts.BaseClass.Should().Be("MockTrackedBase");
        csOpts.ClassAttributes.Should().Contain("[DatabaseContext(\"MockStore\")]");
        csOpts.AdditionalUsings.Should().Contain("MockOrg.Framework.Patterns");
        csOpts.OmitInheritedColumns.Should().Contain("AuditStamp");
    }

    [Fact]
    public async Task ExecuteSync_WithUseFileScopedNamespacesAndDataAnnotations_PassesToApplierOptions()
    {
        var console = new TestConsole();
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);

        var sourceSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();
        var newTable = new TableSchema { Name = "MockTable" };
        newTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Current = "Entities",
            Target = "schema.mmd",
            DryRun = true,
            Yes = true,
            Interactive = false,
            UseFileScopedNamespaces = false,
            UseDataAnnotations = false
        };

        var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);

        exitCode.Should().Be(0);
        mockApplier.LastOptionsUsed.Should().NotBeNull();
        mockApplier.LastOptionsUsed.Should().BeOfType<CSharpApplierOptions>();

        var csOpts = (CSharpApplierOptions)mockApplier.LastOptionsUsed!;
        csOpts.UseFileScopedNamespaces.Should().BeFalse();
        csOpts.UseDataAnnotations.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_MissingCurrent_ReturnsError()
    {
        var console = new TestConsole();
        var handler = new SyncCommandHandler(console: console);
        var options = new SyncCommandOptions
        {
            Current = "",
            Target = "schema.mmd"
        };

        var exitCode = await handler.RunAsync(options);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Current schema path (-c|--current) is required");
    }

    [Fact]
    public async Task RunAsync_MissingTarget_ReturnsError()
    {
        var console = new TestConsole();
        var handler = new SyncCommandHandler(console: console);
        var options = new SyncCommandOptions
        {
            Current = "Entities",
            Target = ""
        };

        var exitCode = await handler.RunAsync(options);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Target schema path (-t|--target) is required");
    }

    [Fact]
    public async Task ExecuteSync_WithForeignKeyPlacementAndTablePrefixes_ConfiguresCSharpApplierOptions()
    {
        var console = new TestConsole();
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);

        var sourceSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();
        var newTable = new TableSchema { Name = "tbl_masked_order" };
        newTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        targetSchema.AddTable(newTable);

        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
        var options = new SyncCommandOptions
        {
            Current = "Entities",
            Target = "schema.mmd",
            DryRun = true,
            Yes = true,
            Interactive = false,
            ForeignKeyPlacement = "scalar",
            TablePrefixes = new List<string> { "tbl_", "px_" }
        };

        var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);

        exitCode.Should().Be(0);
        mockApplier.LastOptionsUsed.Should().NotBeNull();
        mockApplier.LastOptionsUsed.Should().BeOfType<CSharpApplierOptions>();

        var csOpts = (CSharpApplierOptions)mockApplier.LastOptionsUsed!;
        csOpts.ForeignKeyPlacement.Should().Be(ForeignKeyPlacement.Scalar);
        csOpts.TablePrefixes.Should().ContainInOrder("tbl_", "px_");
    }

    [Fact]
    public async Task RunAsync_WithConfigFileSpecifyingForeignKeyPlacementAndTablePrefixes_AppliesOptionsToCSharpApplier()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SchemaAlign_Sync_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var configJson = """
                {
                  "tablePrefixes": ["tbl_"],
                  "csharp": {
                    "foreignKeyPlacement": "scalar",
                    "tablePrefixes": ["px_"]
                  }
                }
                """;
            var configPath = Path.Combine(tempDir, ".schemaalign.json");
            await File.WriteAllTextAsync(configPath, configJson);

            var mermaidSchema = """
                erDiagram
                    tbl_masked_order {
                        nvarchar(36) IdOrder PK
                    }
                """;
            var mmdPath = Path.Combine(tempDir, "target.mmd");
            await File.WriteAllTextAsync(mmdPath, mermaidSchema);

            var currentDir = Path.Combine(tempDir, "Entities");
            Directory.CreateDirectory(currentDir);

            var console = new TestConsole();
            var mockApplier = new MockApplier();
            var registry = new ApplierRegistry();
            registry.Register(TargetType.CSharp, mockApplier);

            var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
            var options = new SyncCommandOptions
            {
                ConfigFile = configPath,
                Current = currentDir,
                Target = mmdPath,
                DryRun = true,
                Yes = true,
                Interactive = false
            };

            var exitCode = await handler.RunAsync(options);

            exitCode.Should().Be(0);
            mockApplier.LastOptionsUsed.Should().NotBeNull();
            var csOpts = (CSharpApplierOptions)mockApplier.LastOptionsUsed!;
            csOpts.ForeignKeyPlacement.Should().Be(ForeignKeyPlacement.Scalar);
            csOpts.TablePrefixes.Should().Contain("tbl_");
            csOpts.TablePrefixes.Should().Contain("px_");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
