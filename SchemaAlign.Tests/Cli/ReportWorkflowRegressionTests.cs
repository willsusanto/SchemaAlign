using System.Text.Json;
using SchemaAlign.Appliers;
using SchemaAlign.Cli.Commands;
using SchemaAlign.Cli.Rendering;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class ReportWorkflowRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SchemaAlign_MaskedReports_{Guid.NewGuid():N}");
    private const string MaskedConnection = "Server=masked-host;Database=masked-store;User ID=masked-user;Password=masked-secret;";

    public ReportWorkflowRegressionTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string ReportPath => Path.Combine(_directory, "report.md");

    private static DatabaseSchema ChangedSchema(int count = 1)
    {
        var schema = new DatabaseSchema();
        for (var i = 1; i <= count; i++)
        {
            var table = new TableSchema { Name = $"tbl_masked_{i:D2}" };
            table.AddColumn(new ColumnSchema { Name = "col_masked_id", Type = StandardType.Int });
            schema.AddTable(table);
        }
        return schema;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiffReport_RedactsConnectionStringsInEitherInput(bool currentIsConnection)
    {
        var handler = new DiffCommandHandler(console: new TestConsole());
        var options = new DiffCommandOptions
        {
            Current = currentIsConnection ? MaskedConnection : "masked-current.mmd",
            Target = currentIsConnection ? "masked-target.mmd" : MaskedConnection,
            ReportPath = ReportPath
        };

        handler.Execute(new DatabaseSchema(), ChangedSchema(), options).Should().Be(0);

        var report = File.ReadAllText(ReportPath);
        report.Should().NotContain("masked-secret").And.NotContain("masked-user");
        report.Should().Contain("database connection");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncReport_RedactsConnectionStringsBeforePreviewAndInFinalReport(bool currentIsConnection)
    {
        var applier = new MaskedApplier();
        applier.BeforePreview = () => File.ReadAllText(ReportPath).Should().NotContain("masked-secret");
        var handler = CreateSyncHandler(applier, new TestConsole());
        var options = new SyncCommandOptions
        {
            Current = currentIsConnection ? MaskedConnection : _directory,
            Target = currentIsConnection ? "masked-target.mmd" : MaskedConnection,
            ReportPath = ReportPath,
            DryRun = true,
            Interactive = false,
            Yes = true
        };

        (await handler.ExecuteAsync(new DatabaseSchema(), ChangedSchema(), options, TargetType.CSharp)).Should().Be(0);

        File.ReadAllText(ReportPath).Should().NotContain("masked-secret").And.NotContain("masked-user")
            .And.Contain("database connection").And.Contain("## File Previews");
    }

    [Theory]
    [InlineData("markdown")]
    [InlineData("md")]
    public void DiffMarkdown_WithReport_EmitsOnlyMarkdown(string format)
    {
        var console = new TestConsole();
        console.Profile.Width = 300;
        var options = new DiffCommandOptions { Output = format, ReportPath = ReportPath };

        new DiffCommandHandler(console: console).Execute(new DatabaseSchema(), ChangedSchema(), options).Should().Be(0);

        console.Output.Should().StartWith("# Schema Diff Summary").And.NotContain("report written to");
        File.Exists(ReportPath).Should().BeTrue();
    }

    [Fact]
    public void DiffExplicitReport_WriteFailure_ReturnsFailureAndKeepsAllConsoleDetails()
    {
        var console = new TestConsole();
        console.Profile.Width = 300;
        var options = new DiffCommandOptions { ReportPath = _directory, Detailed = true };

        var result = new DiffCommandHandler(console: console).Execute(new DatabaseSchema(), ChangedSchema(16), options);

        result.Should().Be(1);
        console.Output.Should().Contain("tbl_masked_16").And.NotContain("Full details in").And.NotContain("more changed tables");
    }

    [Fact]
    public void DiffJson_ExplicitReportWriteFailure_ReturnsFailureAndPreservesJson()
    {
        var console = new TestConsole();
        var options = new DiffCommandOptions { ReportPath = _directory, Output = "json" };

        var result = new DiffCommandHandler(console: console).Execute(new DatabaseSchema(), ChangedSchema(), options);

        result.Should().Be(1);
        using var json = JsonDocument.Parse(console.Output);
        json.RootElement.GetProperty("hasChanges").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SyncExplicitReport_WriteFailure_DoesNotApplyChanges()
    {
        var applier = new MaskedApplier();
        var console = new TestConsole();
        var options = new SyncCommandOptions { Current = _directory, ReportPath = _directory, Interactive = false, Yes = true };

        var result = await CreateSyncHandler(applier, console).ExecuteAsync(new DatabaseSchema(), ChangedSchema(), options, TargetType.CSharp);

        result.Should().Be(1);
        applier.ApplyCalled.Should().BeFalse();
        console.Output.Should().Contain("Could not write report");
    }

    [Fact]
    public async Task SyncExplicitReport_FinalWriteFailure_DoesNotApplyOrClaimPreviewsWereSaved()
    {
        var applier = new MaskedApplier
        {
            BeforePreview = () =>
            {
                File.Delete(ReportPath);
                Directory.CreateDirectory(ReportPath);
            }
        };
        var console = new TestConsole();
        var options = new SyncCommandOptions { Current = _directory, ReportPath = ReportPath, Interactive = false, Yes = true };

        var result = await CreateSyncHandler(applier, console).ExecuteAsync(new DatabaseSchema(), ChangedSchema(), options, TargetType.CSharp);

        result.Should().Be(1);
        applier.ApplyCalled.Should().BeFalse();
        console.Output.Should().NotContain("Full unified diffs written to");
        console.Output.Should().Contain("+masked_content_04");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(15)]
    public void PreviewWithoutSavedReport_KeepsEveryDetailedDiffAvailable(int count)
    {
        var console = new TestConsole();
        console.Profile.Width = 300;

        PreviewConsoleRenderer.RenderPreviews(MaskedApplier.Previews(count), console);

        for (var i = 1; i <= count; i++)
            console.Output.Should().Contain($"+masked_content_{i:D2}");
    }

    [Fact]
    public void DiffExplicitReport_AlignedSchemas_RefreshesStaleReport()
    {
        File.WriteAllText(ReportPath, "stale masked changes");
        var options = new DiffCommandOptions { ReportPath = ReportPath };

        new DiffCommandHandler(console: new TestConsole()).Execute(ChangedSchema(), ChangedSchema(), options).Should().Be(0);

        File.ReadAllText(ReportPath).Should().Contain("`False`").And.NotContain("stale masked changes");
    }

    [Fact]
    public async Task SyncExplicitReport_AlignedSchemas_RefreshesStaleReport()
    {
        File.WriteAllText(ReportPath, "stale masked changes");
        var options = new SyncCommandOptions { Current = _directory, ReportPath = ReportPath, Interactive = false, Yes = true };

        (await CreateSyncHandler(new MaskedApplier(), new TestConsole()).ExecuteAsync(ChangedSchema(), ChangedSchema(), options, TargetType.CSharp)).Should().Be(0);

        File.ReadAllText(ReportPath).Should().Contain("`False`").And.NotContain("stale masked changes");
    }

    private static SyncCommandHandler CreateSyncHandler(MaskedApplier applier, TestConsole console)
    {
        var registry = new ApplierRegistry();
        registry.Register(TargetType.CSharp, applier);
        return new SyncCommandHandler(registry, new SchemaDetectionService(), console);
    }

    private sealed class MaskedApplier : ISchemaApplier
    {
        public string Name => "MaskedApplier";
        public bool ApplyCalled { get; private set; }
        public Action? BeforePreview { get; set; }

        public static IReadOnlyList<FileDiffPreview> Previews(int count = 4) => Enumerable.Range(1, count).Select(i => new FileDiffPreview
        {
            FilePath = $"MaskedEntity_{i:D2}.cs",
            DiffKind = DiffKind.Modified,
            OriginalContent = "masked original",
            NewContent = $"masked_content_{i:D2}",
            UnifiedDiff = $"@@ -1 +1 @@\n-masked_original_{i:D2}\n+masked_content_{i:D2}"
        }).ToList();

        public Task<IReadOnlyList<FileDiffPreview>> PreviewAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
        {
            BeforePreview?.Invoke();
            return Task.FromResult(Previews());
        }

        public Task<ApplierResult> ApplyAsync(SchemaDiff diff, ApplierOptions options, CancellationToken cancellationToken = default)
        {
            ApplyCalled = true;
            return Task.FromResult(new ApplierResult { Success = true });
        }
    }
}
