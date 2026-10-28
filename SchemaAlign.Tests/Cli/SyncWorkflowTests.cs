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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_AllJsonConventions_ReachCSharpGeneration(bool enabled)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SchemaAlign_MaskedConventions_" + Guid.NewGuid().ToString("N"));
        var entities = Path.Combine(directory, "Masked.Entities");
        Directory.CreateDirectory(entities);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "masked.mmd"),
                "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n        string MaskedStamp\n        string MaskedIgnored\n    }");
            await File.WriteAllTextAsync(Path.Combine(directory, "MaskedBase.cs"),
                "namespace Masked.Core; public abstract class MaskedBase { public string MaskedStamp { get; set; } }");
            var json = $$"""
                { "current":"Masked.Entities;MaskedBase.cs", "target":"masked.mmd", "tablePrefixes":["masked_"],
                  "csharp": { "namespace":"Masked.Output", "baseClass":"MaskedBase",
                    "usings":["Masked.Core","using Masked.Core;"],
                    "classAttributes":["MaskedMarker","[MaskedMarker]"],
                    "omitInheritedColumns":["MaskedIgnored"],
                    "useFileScopedNamespaces":{{enabled.ToString().ToLowerInvariant()}},
                    "useDataAnnotations":{{enabled.ToString().ToLowerInvariant()}},
                    "foreignKeyPlacement":"scalar" } }
                """;
            var path = Path.Combine(directory, ".schemaalign.json");
            await File.WriteAllTextAsync(path, json);
            var console = new TestConsole();
            var handler = new SyncCommandHandler(console: console);

            var result = await handler.RunAsync(new SyncCommandOptions { ConfigFile = path, Yes = true, Interactive = false });

            result.Should().Be(0, console.Output);
            var generated = await File.ReadAllTextAsync(Path.Combine(entities, "MaskedRecord.cs"));
            generated.Should().Contain("public class MaskedRecord : MaskedBase");
            generated.Should().NotContain("public string MaskedStamp").And.NotContain("MaskedIgnored");
            generated.Contains("namespace Masked.Output;", StringComparison.Ordinal).Should().Be(enabled);
            generated.Contains("[Key]", StringComparison.Ordinal).Should().Be(enabled);
            System.Text.RegularExpressions.Regex.Matches(generated, @"using Masked.Core;").Count.Should().Be(1);
            System.Text.RegularExpressions.Regex.Matches(generated, @"\[MaskedMarker\]").Count.Should().Be(1);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GuidedSnapshot_ShowsChangesBeforeRequestingDropPermission(bool allowDrops)
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 240;
        console.Input.PushTextWithEnter(allowDrops ? "y" : "n");
        if (allowDrops)
        {
            console.Input.PushKey(ConsoleKey.Enter); // Select the permitted drop.
            console.Input.PushKey(ConsoleKey.Enter); // Finish preview.
        }
        var applier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, applier);
        var current = new DatabaseSchema();
        current.AddTable(new TableSchema { Name = "MaskedPrior" });
        var options = new SyncCommandOptions { Current = "Masked.Entities", Mode = "snapshot", Guided = true };

        var result = await new SyncCommandHandler(registry, console: console)
            .ExecuteAsync(current, new DatabaseSchema(), options, TargetType.CSharp);

        result.Should().Be(0);
        options.AllowDrop.Should().Be(allowDrops);
        applier.ApplyCalled.Should().BeFalse();
        console.Output.IndexOf("Include destructive removals", StringComparison.Ordinal).Should()
            .BeGreaterThan(console.Output.IndexOf("MaskedPrior", StringComparison.Ordinal));
    }
    [Fact]
    public async Task ExecuteSync_ConnectionString_IsPassedIntactToSqlApplier()
    {
        const string connection = "User ID=masked-user;Password=masked-secret;Server=masked-host;Database=masked-store;";
        var console = new TestConsole();
        var applier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.SqlServerDatabase, applier);
        var desired = new DatabaseSchema();
        desired.AddTable(new TableSchema { Name = "MaskedRecord" });

        var result = await new SyncCommandHandler(registry, console: console).ExecuteAsync(
            new DatabaseSchema(), desired,
            new SyncCommandOptions { Current = connection, Interactive = false, Yes = true, DryRun = true },
            TargetType.SqlServerDatabase);

        result.Should().Be(0);
        ((SchemaAlign.Appliers.SqlServer.SqlServerApplierOptions)applier.LastOptionsUsed!).ConnectionString.Should().Be(connection);
        console.Output.Should().NotContain("masked-secret").And.NotContain("masked-user");
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
                  "tablePrefixes": ["tbl_", "px_"],
                  "csharp": {
                    "foreignKeyPlacement": "scalar"

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

    [Fact]
    public async Task ExecuteSync_WithReportPath_GeneratesMarkdownReportAndTruncatesConsoleOutput()
    {
        var console = new TestConsole();
        console.Profile.Width = 240;
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);

        var sourceSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();

        for (var i = 1; i <= 20; i++)
        {
            var t = new TableSchema { Name = $"AlphaTable_{i:D2}" };
            t.AddColumn(new ColumnSchema { Name = "ColA", Type = StandardType.Int });
            targetSchema.AddTable(t);
        }

        var reportTempPath = Path.Combine(Path.GetTempPath(), $"sync_report_test_{Guid.NewGuid():N}.md");

        try
        {
            var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);
            var options = new SyncCommandOptions
            {
                Current = "Entities",
                Target = "schema.mmd",
                Mode = "snapshot",
                ReportPath = reportTempPath,
                DryRun = true,
                Yes = true,
                Interactive = false
            };

            var exitCode = await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);

            exitCode.Should().Be(0);
            File.Exists(reportTempPath).Should().BeTrue();

            var markdownContent = await File.ReadAllTextAsync(reportTempPath);
            markdownContent.Should().Contain("# Schema Sync Report");
            markdownContent.Should().Contain("AlphaTable_01");
            markdownContent.Should().Contain("AlphaTable_20");
            markdownContent.Should().Contain("## File Previews");

            // Console output should be truncated to top 15 tables
            console.Output.Should().Contain("AlphaTable_01");
            console.Output.Should().Contain("AlphaTable_15");
            console.Output.Should().NotContain("AlphaTable_16");
            console.Output.Should().Contain("5 more changed tables");
            console.Output.Should().Contain(reportTempPath);
        }
        finally
        {
            if (File.Exists(reportTempPath))
            {
                File.Delete(reportTempPath);
            }
        }
    }

    [Fact]
    public void ExecuteDiff_WhenReportPathHasBracketsAndWriteFails_DisplaysWarningWithoutMarkupParseException()
    {
        var console = new TestConsole();
        console.Profile.Width = 300;
        var handler = new DiffCommandHandler(new SchemaDetectionService(), console);

        var sourceSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();
        var table = new TableSchema { Name = "MaskedTable" };
        table.AddColumn(new ColumnSchema { Name = "MaskedCol", Type = StandardType.Int });
        targetSchema.AddTable(table);

        var bracketDir = Path.Combine(Path.GetTempPath(), $"[agent_diff_report_{Guid.NewGuid():N}]");
        Directory.CreateDirectory(bracketDir);

        try
        {
            var options = new DiffCommandOptions
            {
                Current = "Entities",
                Target = "schema.mmd",
                ReportPath = bracketDir
            };

            var act = () => handler.Execute(sourceSchema, targetSchema, options);
            act.Should().NotThrow();
            console.Output.Should().Contain("Warning: Could not write report to");
            console.Output.Should().Contain(bracketDir);
        }
        finally
        {
            if (Directory.Exists(bracketDir))
            {
                Directory.Delete(bracketDir, true);
            }
        }
    }

    [Fact]
    public async Task ExecuteSync_WhenReportPathHasBracketsAndWriteFails_DisplaysWarningWithoutMarkupParseException()
    {
        var console = new TestConsole();
        console.Profile.Width = 300;
        var mockApplier = new MockApplier();
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, mockApplier);
        var handler = new SyncCommandHandler(registry, new SchemaDetectionService(), console);

        var sourceSchema = new DatabaseSchema();
        var targetSchema = new DatabaseSchema();
        var table = new TableSchema { Name = "MaskedTable" };
        table.AddColumn(new ColumnSchema { Name = "MaskedCol", Type = StandardType.Int });
        targetSchema.AddTable(table);

        var bracketDir = Path.Combine(Path.GetTempPath(), $"[agent_sync_report_{Guid.NewGuid():N}]");
        Directory.CreateDirectory(bracketDir);

        try
        {
            var options = new SyncCommandOptions
            {
                Current = "Entities",
                Target = "schema.mmd",
                ReportPath = bracketDir,
                DryRun = true,
                Yes = true,
                Interactive = false
            };

            var act = async () => await handler.ExecuteAsync(sourceSchema, targetSchema, options, TargetType.CSharp);
            await act.Should().NotThrowAsync();
            console.Output.Should().Contain("Warning: Could not write report to");
            console.Output.Should().Contain(bracketDir);
        }
        finally
        {
            if (Directory.Exists(bracketDir))
            {
                Directory.Delete(bracketDir, true);
            }
        }
    }
}
