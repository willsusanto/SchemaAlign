using System.CommandLine;
using SchemaAlign.Cli;
using SchemaAlign.Cli.Commands;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class CommandLineParsingTests
{
    [Theory]
    [InlineData("sync", "--namespace", "Masked.Entities")]
    [InlineData("sync", "--ns", "Masked.Entities")]
    [InlineData("sync", "--base-class", "MaskedBase")]
    [InlineData("sync", "--using", "Masked.Core")]
    [InlineData("sync", "--class-attribute", "MaskedMarker")]
    [InlineData("sync", "--class-attr", "MaskedMarker")]
    [InlineData("sync", "--foreign-key-placement", "scalar")]
    [InlineData("sync", "--fk-placement", "scalar")]
    [InlineData("sync", "--table-prefix", "masked_")]
    [InlineData("diff", "--table-prefixes", "masked_")]
    [InlineData("sync", "--interactive", "false")]
    [InlineData("sync", "--no-drop", "")]
    [InlineData("diff", "--report-file", "masked.md")]
    [InlineData("sync", "--report-file", "masked.md")]
    public void RemovedOptions_AreRejected(string command, string option, string value)
    {
        var arguments = new List<string> { command, "-c", "masked.cs", "-t", "masked.mmd", option };
        if (value.Length > 0) arguments.Add(value);
        var result = CommandLineConfiguration.CreateRootCommand().Parse(arguments.ToArray());
        result.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("diff", "--mode", "invalid")]
    [InlineData("sync", "--mode", "invalid")]
    [InlineData("diff", "--output", "invalid")]
    [InlineData("inspect", "--output", "markdown")]
    public void InvalidChoice_IsRejectedBeforeRunningHandler(string command, string option, string value)
    {
        var result = CommandLineConfiguration.CreateRootCommand().Parse(new[] { command, "-c", "masked.cs", option, value });
        result.Errors.Should().NotBeEmpty();
    }
    [Theory]
    [InlineData("diff", "--current,--target,--mode,--output,--detailed,--config,--report")]
    [InlineData("sync", "--current,--target,--output-file,--mode,--allow-drop,--dry-run,--yes,--config,--report")]
    [InlineData("inspect", "--current,--output")]
    public void Commands_ExposeOnlyTheDocumentedOptions(string command, string names)
    {
        var root = CommandLineConfiguration.CreateRootCommand();
        var selected = root.Subcommands.Single(item => item.Name == command);
        selected.Options.Where(option => option.Name != "--help").Select(option => option.Name)
            .Should().BeEquivalentTo(names.Split(','));
    }
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
    public async Task SyncCommand_ParsesAllowDropFlag()
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
    public async Task DiffCommand_AllowsOmissionOfSourceAndTarget_WhenUsingConfig()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff --config myconfig.json").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().BeEmpty();
        capturedOptions.Target.Should().BeEmpty();
        capturedOptions.ConfigFile.Should().Be("myconfig.json");
    }

    [Fact]
    public async Task SyncCommand_AllowsOmissionOfSourceAndTarget_WhenUsingConfig()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse("sync --config myconfig.json").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Current.Should().BeEmpty();
        capturedOptions.Target.Should().BeEmpty();
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

    [Fact]
    public async Task DiffCommand_ParsesReportFlag()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff -c schema.mmd -t ./Entities -r reports/diff.md").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ReportPath.Should().Be("reports/diff.md");
    }

    [Fact]
    public async Task SyncCommand_ParsesReportFlag()
    {
        SyncCommandOptions? capturedOptions = null;

        var mockHandler = new TestSyncHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: mockHandler);
        var exitCode = await root.Parse("sync -c schema.mmd -t ./Entities --report reports/sync.md -y").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.ReportPath.Should().Be("reports/sync.md");
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
}
