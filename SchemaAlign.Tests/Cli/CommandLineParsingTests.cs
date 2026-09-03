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
        var exitCode = await root.Parse("diff -s schema.mmd -t ./Entities").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Source.Should().Be("schema.mmd");
        capturedOptions.Target.Should().Be("./Entities");
        capturedOptions.Mode.Should().Be("incremental");
        capturedOptions.Output.Should().Be("console");
        capturedOptions.Detailed.Should().BeFalse();
    }

    [Fact]
    public async Task DiffCommand_ParsesFromAndToAliases()
    {
        DiffCommandOptions? capturedOptions = null;

        var mockHandler = new TestDiffHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: mockHandler);
        var exitCode = await root.Parse("diff --from ./Entities --to schema.mmd").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Source.Should().Be("./Entities");
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
        var exitCode = await root.Parse("diff -s schema.mmd -t ./Entities -m snapshot -o json --detailed").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Mode.Should().Be("snapshot");
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
        var exitCode = await root.Parse("sync -s schema.mmd -t ./Entities --dry-run -y").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Source.Should().Be("schema.mmd");
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
        var exitCode = await root.Parse("sync -s schema.mmd -t ./Entities --allow-drop").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.AllowDrop.Should().BeTrue();
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
        var exitCode = await root.Parse("sync -s schema.mmd -t ./Entities --namespace MockOrg.Data.Entities").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Namespace.Should().Be("MockOrg.Data.Entities");
    }

    [Fact]
    public async Task InspectCommand_ParsesSourceAndOutput()
    {
        InspectCommandOptions? capturedOptions = null;

        var mockHandler = new TestInspectHandler(opts =>
        {
            capturedOptions = opts;
            return Task.FromResult(0);
        });

        var root = CommandLineConfiguration.CreateRootCommand(inspectHandler: mockHandler);
        var exitCode = await root.Parse("inspect -s schema.mmd -o json").InvokeAsync();

        exitCode.Should().Be(0);
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Source.Should().Be("schema.mmd");
        capturedOptions.Output.Should().Be("json");
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
