using System.CommandLine;
using SchemaAlign.Cli;
using SchemaAlign.Cli.Commands;
using SchemaAlign.Cli.Services;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class CliEndToEndTests : IDisposable
{
    private readonly string _testDir;

    public CliEndToEndTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_CliE2E_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("console", "incremental")]
    [InlineData("json", "snapshot")]
    [InlineData("markdown", "incremental")]
    [InlineData("md", "snapshot")]
    public async Task DiffCommand_AllRetainedOptions_RunThroughParserAndWriteReport(string output, string mode)
    {
        var current = Path.Combine(_testDir, "masked-current.mmd");
        var desired = Path.Combine(_testDir, "masked-desired.mmd");
        var report = Path.Combine(_testDir, "masked-report.md");
        var config = Path.Combine(_testDir, ".schemaalign.json");
        File.WriteAllText(current, "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n    }");
        File.WriteAllText(desired, "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n        string MaskedLabel\n    }");
        File.WriteAllText(config, """{"current":"masked-unused.cs","target":"masked-unused.mmd"}""");
        var console = new TestConsole();
        var root = CommandLineConfiguration.CreateRootCommand(diffHandler: new DiffCommandHandler(console: console));

        var result = await root.Parse(new[] { "diff", "--current", current, "--target", desired,
            "--mode", mode, "--output", output, "--detailed", "--config", config, "--report", report }).InvokeAsync();

        result.Should().Be(0, console.Output);
        File.ReadAllText(report).Should().Contain("MaskedLabel");
        console.Output.Should().Contain("MaskedLabel");
        if (output == "console") console.Output.Should().Contain("Comparison mode: " + mode);
        if (output == "json")
        {
            using var document = System.Text.Json.JsonDocument.Parse(console.Output);
            document.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SyncCommand_Automation_PreviewsOrAppliesWithoutPrompts(bool dryRun)
    {
        var entities = Path.Combine(_testDir, "Masked.Entities");
        Directory.CreateDirectory(entities);
        var desired = Path.Combine(_testDir, "masked-desired.mmd");
        var config = Path.Combine(_testDir, ".schemaalign.json");
        var report = Path.Combine(_testDir, "masked-sync.md");
        File.WriteAllText(desired, "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n    }");
        File.WriteAllText(config, """{"csharp":{"namespace":"Masked.Output"}}""");
        var console = new TestConsole();
        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: new SyncCommandHandler(console: console));
        var arguments = new List<string> { "sync", "--current", entities, "--target", desired,
            "--mode", "incremental", "--yes", "--allow-drop", "--report", report, "--config", config };
        if (dryRun) arguments.Add("--dry-run");

        var result = await root.Parse(arguments.ToArray()).InvokeAsync();

        result.Should().Be(0, console.Output);
        console.Output.Should().Contain("Comparison mode: incremental").And.Contain("Write destination:");
        File.Exists(Path.Combine(entities, "MaskedRecord.cs")).Should().Be(!dryRun);
        File.ReadAllText(report).Should().Contain("MaskedRecord");
        console.Output.Should().NotContain("What would you like to do with these changes?");
        if (!dryRun) File.ReadAllText(Path.Combine(entities, "MaskedRecord.cs")).Should().Contain("namespace Masked.Output;");
    }

    [Fact]
    public async Task SyncCommand_SqlOutputFile_ExportsWithoutEditingInput()
    {
        var current = Path.Combine(_testDir, "masked-current.sql");
        var desired = Path.Combine(_testDir, "masked-desired.mmd");
        var output = Path.Combine(_testDir, "masked-migration.sql");
        const string original = "CREATE TABLE MaskedRecord (MaskedKey int NOT NULL PRIMARY KEY);";
        File.WriteAllText(current, original);
        File.WriteAllText(desired, "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n        string MaskedLabel\n    }");
        var console = new TestConsole();
        var root = CommandLineConfiguration.CreateRootCommand(syncHandler: new SyncCommandHandler(console: console));

        var result = await root.Parse(new[] { "sync", "-c", current, "-t", desired,
            "--output-file", output, "--mode", "snapshot", "-y" }).InvokeAsync();

        result.Should().Be(0, console.Output);
        File.ReadAllText(output).Should().Contain("MaskedLabel");
        File.ReadAllText(current).Should().Be(original);
    }

    [Theory]
    [InlineData("console")]
    [InlineData("json")]
    public async Task InspectCommand_AllOptions_ReadsSchemaThroughParser(string output)
    {
        var path = Path.Combine(_testDir, "masked.mmd");
        File.WriteAllText(path, "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n    }");
        var console = new TestConsole();
        var root = CommandLineConfiguration.CreateRootCommand(inspectHandler: new InspectCommandHandler(console: console));

        var result = await root.Parse(new[] { "inspect", "--current", path, "--output", output }).InvokeAsync();

        result.Should().Be(0, console.Output);
        console.Output.Should().Contain("MaskedRecord").And.Contain("MaskedKey");
    }

    [Theory]
    [InlineData("diff")]
    [InlineData("sync")]
    public async Task InvalidConfiguration_ReturnsCommandErrorWithoutModifyingSchemas(string command)
    {
        var config = Path.Combine(_testDir, ".schemaalign.json");
        File.WriteAllText(config, """{"mode":"snapshot"}""");
        var console = new TestConsole();
        var root = CommandLineConfiguration.CreateRootCommand(
            diffHandler: new DiffCommandHandler(console: console), syncHandler: new SyncCommandHandler(console: console));

        var result = await root.Parse(new[] { command, "--config", config }).InvokeAsync();

        result.Should().Be(1);
        console.Output.Should().Contain("--mode");
    }
    [Fact]
    public async Task DiffCommand_CSharpDirectoryVsMermaid_RendersConsoleOutput()
    {
        // 1. Create current base C# entity directory
        var csDir = Path.Combine(_testDir, "Entities");
        Directory.CreateDirectory(csDir);
        var csContent = @"
public class CustomerTable
{
    public int Id { get; set; }
    public string Name { get; set; }
}
";
        File.WriteAllText(Path.Combine(csDir, "CustomerTable.cs"), csContent);

        // 2. Create desired target mermaid file
        var mermaidPath = Path.Combine(_testDir, "schema.mmd");
        var mermaidContent = @"
erDiagram
    CustomerTable {
        int Id PK
        string Name ""100""
        string Email ""150""
    }
";
        File.WriteAllText(mermaidPath, mermaidContent);

        // 3. Execute diff command handler with TestConsole: Source = Current (C#), Target = Desired (Mermaid)
        var console = new TestConsole();
        var handler = new DiffCommandHandler(new SchemaDetectionService(), console);

        var exitCode = await handler.RunAsync(new DiffCommandOptions
        {
            Current = csDir,
            Target = mermaidPath,
            Mode = "incremental",
            Detailed = true
        });

        exitCode.Should().Be(0);
        var output = console.Output;
        output.Should().Contain("CustomerTable");
        output.Should().Contain("Email");
    }

    [Fact]
    public async Task DiffCommand_MultiPathCSharpDirectoriesVsMermaid_RendersConsoleOutput()
    {
        // 1. Create base folder with BaseModel
        var baseDir = Path.Combine(_testDir, "Base");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "BaseModel.cs"), @"
public abstract class BaseModel
{
    public int Id { get; set; }
}
");

        // 2. Create target entities folder with CustomerTable
        var entitiesDir = Path.Combine(_testDir, "Entities");
        Directory.CreateDirectory(entitiesDir);
        File.WriteAllText(Path.Combine(entitiesDir, "CustomerTable.cs"), @"
public class CustomerTable : BaseModel
{
    public string Name { get; set; }
}
");

        // 3. Create sibling DB folder that should be ignored
        var siblingDir = Path.Combine(_testDir, "OtherDb");
        Directory.CreateDirectory(siblingDir);
        File.WriteAllText(Path.Combine(siblingDir, "OtherTable.cs"), @"
public class OtherTable
{
    public int OtherId { get; set; }
}
");

        // 4. Create desired mermaid file
        var mermaidPath = Path.Combine(_testDir, "schema.mmd");
        File.WriteAllText(mermaidPath, @"
erDiagram
    CustomerTable {
        int Id PK
        string Name ""100""
        string Email ""150""
    }
");

        var console = new TestConsole();
        var handler = new DiffCommandHandler(new SchemaDetectionService(), console);

        var exitCode = await handler.RunAsync(new DiffCommandOptions
        {
            Current = $"{entitiesDir};{baseDir}",
            Target = mermaidPath,
            Mode = "incremental",
            Detailed = true
        });

        exitCode.Should().Be(0);
        var output = console.Output;
        output.Should().Contain("CustomerTable");
        output.Should().Contain("Email");
        output.Should().NotContain("OtherTable");
    }

    [Fact]
    public async Task InspectCommand_MermaidFile_RendersTableTree()
    {
        var mermaidPath = Path.Combine(_testDir, "schema.mmd");
        var mermaidContent = @"
erDiagram
    ProductTable {
        int ProductId PK
        decimal Price ""18,2""
    }
";
        File.WriteAllText(mermaidPath, mermaidContent);

        var console = new TestConsole();
        var handler = new InspectCommandHandler(new SchemaDetectionService(), console);

        var exitCode = await handler.RunAsync(new InspectCommandOptions
        {
            Current = mermaidPath,
            Output = "console"
        });

        exitCode.Should().Be(0);
        console.Output.Should().Contain("ProductTable");
        console.Output.Should().Contain("ProductId");
        console.Output.Should().Contain("Price");
    }

    [Fact]
    public async Task DiffCommand_MissingFile_ReturnsErrorCode()
    {
        var console = new TestConsole();
        var handler = new DiffCommandHandler(new SchemaDetectionService(), console);

        var exitCode = await handler.RunAsync(new DiffCommandOptions
        {
            Current = Path.Combine(_testDir, "non_existent.mmd"),
            Target = Path.Combine(_testDir, "Entities")
        });

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error");
    }
}
