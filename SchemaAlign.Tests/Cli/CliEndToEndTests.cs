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
            Source = csDir,
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
            Source = $"{entitiesDir};{baseDir}",
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
            Source = mermaidPath,
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
            Source = Path.Combine(_testDir, "non_existent.mmd"),
            Target = Path.Combine(_testDir, "Entities")
        });

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Error");
    }
}
