using SchemaAlign.Cli.Commands;
using Spectre.Console.Testing;

namespace SchemaAlign.Tests.Cli;

public class WizardCommandHandlerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SchemaAlign_Wizard_" + Guid.NewGuid().ToString("N"));

    public WizardCommandHandlerTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(".mmd", "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n    }")]
    [InlineData(".mermaid", "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n    }")]
    [InlineData(".cs", "public class MaskedRecord { public int MaskedKey { get; set; } }")]
    [InlineData(".sql", "CREATE TABLE MaskedRecord (MaskedKey int NOT NULL PRIMARY KEY);")]
    public async Task Inspect_MissingFile_RetriesAndReadsCorrectedPath(string extension, string content)
    {
        var validPath = Path.Combine(_directory, "masked" + extension);
        File.WriteAllText(validPath, content);
        var console = CreateConsole(2);
        console.Input.PushTextWithEnter(Path.Combine(_directory, "missing" + extension));
        console.Input.PushTextWithEnter(validPath);

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        console.Output.Should().Contain("File not found:").And.Contain("MaskedRecord");
    }

    [Fact]
    public async Task Inspect_MissingDirectory_RetriesAndReadsCorrectedDirectory()
    {
        File.WriteAllText(Path.Combine(_directory, "MaskedRecord.cs"),
            "public class MaskedRecord { public int MaskedKey { get; set; } }");
        var console = CreateConsole(2);
        console.Input.PushTextWithEnter(Path.Combine(_directory, "missing-models"));
        console.Input.PushTextWithEnter(_directory);

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        console.Output.Should().Contain("Directory not found:").And.Contain("MaskedRecord");
    }

    [Fact]
    public async Task Diff_ValidatesBothPathsBeforeAskingForMode()
    {
        var path = Path.Combine(_directory, "masked.sql");
        File.WriteAllText(path, "CREATE TABLE MaskedRecord (MaskedKey int NOT NULL PRIMARY KEY);");
        var console = CreateConsole(0);
        console.Input.PushTextWithEnter(Path.Combine(_directory, "missing-current.sql"));
        console.Input.PushTextWithEnter(path);
        console.Input.PushTextWithEnter(Path.Combine(_directory, "missing-desired.sql"));
        console.Input.PushTextWithEnter(path);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("n");

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        var output = console.Output;
        output.IndexOf("File not found:", StringComparison.Ordinal).Should()
            .BeLessThan(output.IndexOf("Enter Desired", StringComparison.Ordinal));
        output.LastIndexOf("File not found:", StringComparison.Ordinal).Should()
            .BeLessThan(output.IndexOf("Select diff mode:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_ValidatesPathsBeforeAskingForApplyOptions()
    {
        var path = Path.Combine(_directory, "masked.sql");
        File.WriteAllText(path, "CREATE TABLE MaskedRecord (MaskedKey int NOT NULL PRIMARY KEY);");
        var console = CreateConsole(1);
        console.Input.PushTextWithEnter(Path.Combine(_directory, "missing-current.sql"));
        console.Input.PushTextWithEnter(path);
        console.Input.PushTextWithEnter(Path.Combine(_directory, "missing-desired.sql"));
        console.Input.PushTextWithEnter(path);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("n");
        console.Input.PushTextWithEnter("y");

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        console.Output.LastIndexOf("File not found:", StringComparison.Ordinal).Should()
            .BeLessThan(console.Output.IndexOf("Allow destructive drops", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inspect_QuotedRelativePath_ShowsWorkingDirectoryAndReadsSchema()
    {
        var path = Path.Combine(_directory, "masked.sql");
        File.WriteAllText(path, "CREATE TABLE MaskedRecord (MaskedKey int NOT NULL PRIMARY KEY);");
        var console = CreateConsole(2);
        console.Input.PushTextWithEnter("  \"" + Path.GetRelativePath(Directory.GetCurrentDirectory(), path) + "\"  ");

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        console.Output.Should().Contain("Relative paths start from:")
            .And.Contain(Directory.GetCurrentDirectory()).And.Contain("MaskedRecord");
    }
    private static TestConsole CreateConsole(int downPresses)
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 240;
        for (var i = 0; i < downPresses; i++) console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        return console;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
