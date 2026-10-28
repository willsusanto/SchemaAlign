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

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        var output = console.Output;
        output.IndexOf("File not found:", StringComparison.Ordinal).Should()
            .BeLessThan(output.IndexOf("Enter Desired", StringComparison.Ordinal));
        output.LastIndexOf("File not found:", StringComparison.Ordinal).Should()
            .BeLessThan(output.IndexOf("What does your desired schema describe?", StringComparison.Ordinal));
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

        var result = await new WizardCommandHandler(console: console).RunAsync();

        result.Should().Be(0);
        console.Output.LastIndexOf("File not found:", StringComparison.Ordinal).Should()
            .BeLessThan(console.Output.IndexOf("What does your desired schema describe?", StringComparison.Ordinal));
        console.Output.Should().NotContain("Allow destructive drops").And.NotContain("Run in Dry-Run");
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
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task Sync_ConfiguredPaths_ReviewsBeforeChoosingPreviewApplyOrCancel(int finalChoice, bool applies)
    {
        var entities = Path.Combine(_directory, "Masked.Entities");
        Directory.CreateDirectory(entities);
        File.WriteAllText(Path.Combine(_directory, "masked.mmd"),
            "erDiagram\n    MaskedRecord {\n        int MaskedKey PK\n    }");
        var configPath = Path.Combine(_directory, ".schemaalign.json");
        File.WriteAllText(configPath, """{"current":"Masked.Entities","target":"masked.mmd"}""");
        var console = CreateConsole(1);
        console.Input.PushKey(ConsoleKey.Enter); // Incremental.
        console.Input.PushKey(ConsoleKey.Enter); // Keep preselected additions.
        for (var i = 0; i < finalChoice; i++) console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await new WizardCommandHandler(console: console, configFile: configPath).RunAsync();

        result.Should().Be(0, console.Output);
        File.Exists(Path.Combine(entities, "MaskedRecord.cs")).Should().Be(applies);
        console.Output.Should().Contain("Comparison mode: incremental").And.Contain("Write destination:");
        console.Output.Should().NotContain("Enter Current").And.NotContain("Run in Dry-Run");
        console.Output.IndexOf("What would you like to do with these changes?", StringComparison.Ordinal)
            .Should().BeGreaterThan(console.Output.IndexOf("MaskedRecord", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diff_ConfiguredPaths_UsesOnlyTheWizardMode(bool snapshot)
    {
        var entities = Path.Combine(_directory, "Masked.Entities");
        Directory.CreateDirectory(entities);
        File.WriteAllText(Path.Combine(entities, "MaskedPrior.cs"), "public class MaskedPrior { public int MaskedKey { get; set; } }");
        File.WriteAllText(Path.Combine(_directory, "masked.mmd"), "erDiagram\n    MaskedNew {\n        int MaskedKey PK\n    }");
        var path = Path.Combine(_directory, ".schemaalign.json");
        File.WriteAllText(path, """{"current":"Masked.Entities","target":"masked.mmd"}""");
        var console = CreateConsole(0);
        if (snapshot) console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);

        var result = await new WizardCommandHandler(console: console, configFile: path).RunAsync();

        result.Should().Be(0, console.Output);
        console.Output.Should().Contain("Comparison mode: " + (snapshot ? "snapshot" : "incremental"));
        console.Output.Contains("MaskedPrior", StringComparison.Ordinal).Should().Be(snapshot);
        File.Exists(Path.Combine(entities, "MaskedPrior.cs")).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidConfiguration_ShowsMigrationErrorBeforeAskingForPaths()
    {
        var path = Path.Combine(_directory, ".schemaalign.json");
        File.WriteAllText(path, """{"mode":"snapshot"}""");
        var console = CreateConsole(0);

        var result = await new WizardCommandHandler(console: console, configFile: path).RunAsync();

        result.Should().Be(1);
        console.Output.Should().Contain("--mode").And.NotContain("Enter Current");
    }

    [Fact]
    public async Task Exit_DoesNotLoadConfiguration()
    {
        var console = CreateConsole(3);
        var result = await new WizardCommandHandler(console: console, configFile: "masked-missing.json").RunAsync();
        result.Should().Be(0);
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
