using System.CommandLine;
using SchemaAlign.Cli;
using SchemaAlign.Cli.Commands;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class CommandLineParsingTests
{
    [Fact]
    public async Task DiffCommand_ParsesArgumentsAndDefaultsToIncrementalMode()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff -c schema.mmd -t ./Entities").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.Mode.Should().Be("incremental");
        capturedOptions.Output.Should().Be("console");
        capturedOptions.Detailed.Should().BeFalse();
    }

    [Fact]
    public async Task DiffCommand_ParsesLongCurrentAndTargetFlags()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff --current ./Entities --target schema.mmd").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("./Entities");
        capturedOptions.Target.Should().Be("schema.mmd");
    }

    [Fact]
    public async Task DiffCommand_ParsesSnapshotModeAndDetailedFlags()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff -c schema.mmd -t ./Entities -m snapshot -o json --detailed").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.Mode.Should().Be("snapshot");
        capturedOptions.Output.Should().Be("json");
        capturedOptions.Detailed.Should().BeTrue();
    }

    [Fact]
    public async Task SyncCommand_ParsesFlagsAndDefaultIncremental()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse("sync -c schema.mmd -t ./Entities --dry-run -y").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.Mode.Should().Be("incremental");
        capturedOptions.DryRun.Should().BeTrue();
        capturedOptions.Yes.Should().BeTrue();
        capturedOptions.Interactive.Should().BeFalse();
        capturedOptions.AllowDrop.Should().BeFalse();
    }

    [Fact]
    public async Task SyncCommand_ParsesAllowDropAndNoDropFlags()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse("sync -c schema.mmd -t ./Entities --allow-drop").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.AllowDrop.Should().BeTrue();
    }

    [Fact]
    public async Task SyncCommand_ParsesNamespaceFlag()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse("sync -c schema.mmd -t ./Entities --namespace MockOrg.Data.Entities").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.Namespace.Should().Be("MockOrg.Data.Entities");
    }

    [Fact]
    public async Task SyncCommand_ParsesConfigBaseClassUsingsAndAttributesFlags()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse(new[]
        {
            "sync", "-c", "schema.mmd", "-t", "./Entities",
            "--config", "custom_config.json",
            "--base-class", "MockEntityBase",
            "--using", "MockOrg.Core",
            "--using", "MockOrg.Pattern",
            "--class-attr", "[CustomGroup(\"Core\")]"
        }).InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.ConfigFile.Should().Be("custom_config.json");
        capturedOptions.BaseClass.Should().Be("MockEntityBase");
        capturedOptions.Usings.Should().Contain("MockOrg.Core");
        capturedOptions.Usings.Should().Contain("MockOrg.Pattern");
        capturedOptions.ClassAttributes.Should().Contain("[CustomGroup(\"Core\")]");
    }

    [Fact]
    public async Task DiffCommand_ParsesConfigFile()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff -c schema.mmd -t ./Entities --config myconfig.json").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.ConfigFile.Should().Be("myconfig.json");
    }

    [Fact]
    public async Task InspectCommand_ParsesCurrentAndOutput()
    {
        InspectCommandOptions? capturedOptions = null;

        var mockHandler = new TestInspectHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(inspectHandler: mockHandler);
        var exitCode = await root.Parse("inspect -c schema.mmd -o json").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("schema.mmd");
        capturedOptions.Output.Should().Be("json");
    }

    [Fact]
    public async Task ExportCommand_ParsesAllOptionsAndAliases()
    {
        ExportCommandOptions? capturedOptions = null;

        var mockHandler = new TestExportHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(exportHandler: mockHandler);
        var exitCode = await root.Parse("export -t schema.mmd -o out.xlsx -c custom.json --aid 2026 --ip db.internal --db PROD_DB --title \"Custom Title\"").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Target.Should().Be("schema.mmd");
        capturedOptions.Output.Should().Be("out.xlsx");
        capturedOptions.Config.Should().Be("custom.json");
        capturedOptions.Aid.Should().Be("2026");
        capturedOptions.Ip.Should().Be("db.internal");
        capturedOptions.Db.Should().Be("PROD_DB");
        capturedOptions.Title.Should().Be("Custom Title");
    }

    [Fact]
    public async Task DiffCommand_ParsesCurrentAndTargetOptionsAndAliases()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff -c ./Entities -t schema.mmd").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("./Entities");
        capturedOptions.Target.Should().Be("schema.mmd");
    }

    [Fact]
    public async Task SyncCommand_ParsesCurrentAndOutputFileOptions()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse("sync --current \"Server=sql;Database=TestDb;\" --target schema.mmd -o ./migration.sql -y").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().Be("Server=sql;Database=TestDb;");
        capturedOptions.Target.Should().Be("schema.mmd");
        capturedOptions.OutputFile.Should().Be("./migration.sql");
    }

    private class TestDiffHandler : DiffCommandHandler
    {
        private readonly Func<DiffCommandOptions, Task<int>> _action;
        public TestDiffHandler(Func<DiffCommandOptions, Task<int>> action) => _action = action;
        public override Task<int> RunAsync(DiffCommandOptions options, CancellationToken ct = default) => _action(options);
    }

    private class TestSyncHandler : SyncCommandHandler
    {
        private readonly Func<SyncCommandOptions, Task<int>> _action;
        public TestSyncHandler(Func<SyncCommandOptions, Task<int>> action) => _action = action;
        public override Task<int> RunAsync(SyncCommandOptions options, CancellationToken ct = default) => _action(options);
    }

    private class TestInspectHandler : InspectCommandHandler
    {
        private readonly Func<InspectCommandOptions, Task<int>> _action;
        public TestInspectHandler(Func<InspectCommandOptions, Task<int>> action) => _action = action;
        public override Task<int> RunAsync(InspectCommandOptions options, CancellationToken ct = default) => _action(options);
    }

    private class TestExportHandler : ExportCommandHandler
    {
        private readonly Func<ExportCommandOptions, Task<int>> _action;
        public TestExportHandler(Func<ExportCommandOptions, Task<int>> action) => _action = action;
        public override Task<int> RunAsync(ExportCommandOptions options, CancellationToken ct = default) => _action(options);
    }
}
