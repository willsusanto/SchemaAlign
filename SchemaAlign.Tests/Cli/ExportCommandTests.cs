using SchemaAlign.Cli;
using SchemaAlign.Cli.Commands;
using Spectre.Console.Testing;
using System.CommandLine;

namespace SchemaAlign.Tests.Cli;

public class ExportCommandTests : IDisposable
{
    private readonly string _tempDir;

    public ExportCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_CliExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public async Task ExportCommand_WhenSourceIsNotMermaid_ReturnsNonZeroExitCode()
    {
        var nonMermaidFile = Path.Combine(_tempDir, "Entity.cs");
        File.WriteAllText(nonMermaidFile, "public class Entity { public int Id { get; set; } }");

        var outputFile = Path.Combine(_tempDir, "dictionary.xlsx");

        var rootCommand = CommandLineConfiguration.CreateRootCommand();
        var exitCode = await rootCommand.Parse($"export --target \"{nonMermaidFile}\" --output \"{outputFile}\"").InvokeAsync();

        exitCode.Should().NotBe(0);
        File.Exists(outputFile).Should().BeFalse();
    }

    [Fact]
    public async Task ExportCommand_WithValidMermaid_GeneratesXlsxAndReturnsZero()
    {
        var mermaidFile = Path.Combine(_tempDir, "model.mmd");
        File.WriteAllText(mermaidFile, """
            erDiagram
                MockEntity {
                    nvarchar(36) IdMock PK
                    nvarchar(50) Name
                }
            """);

        var outputFile = Path.Combine(_tempDir, "dictionary.xlsx");

        var rootCommand = CommandLineConfiguration.CreateRootCommand();
        var exitCode = await rootCommand.Parse($"export -t \"{mermaidFile}\" -o \"{outputFile}\" --aid 5555 --db TEST_DB").InvokeAsync();

        exitCode.Should().Be(0);
        File.Exists(outputFile).Should().BeTrue();
    }

    [Fact]
    public async Task ExportCommand_WithMermaidFileExtension_GeneratesXlsxSuccessfully()
    {
        var mermaidFile = Path.Combine(_tempDir, "model.mermaid");
        File.WriteAllText(mermaidFile, """
            erDiagram
                MockEntity {
                    nvarchar(36) IdMock PK
                }
            """);

        var outputFile = Path.Combine(_tempDir, "out_from_mermaid.xlsx");

        var rootCommand = CommandLineConfiguration.CreateRootCommand();
        var exitCode = await rootCommand.Parse($"export -t \"{mermaidFile}\" -o \"{outputFile}\"").InvokeAsync();

        exitCode.Should().Be(0);
        File.Exists(outputFile).Should().BeTrue();
    }

    [Fact]
    public async Task WizardCommandHandler_Option4_InvokesExportHandlerSuccessfully()
    {
        var mermaidFile = Path.Combine(_tempDir, "wizard_model.mmd");
        File.WriteAllText(mermaidFile, """
            erDiagram
                MockEntity {
                    nvarchar(36) IdMock PK
                    nvarchar(50) Name
                }
            """);

        var outputFile = Path.Combine(_tempDir, "wizard_output.xlsx");

        var testConsole = new TestConsole();
        testConsole.Profile.Capabilities.Interactive = true;
        // Option 4 is 3 DownArrows away from Option 1
        testConsole.Input.PushKey(ConsoleKey.DownArrow);
        testConsole.Input.PushKey(ConsoleKey.DownArrow);
        testConsole.Input.PushKey(ConsoleKey.DownArrow);
        testConsole.Input.PushKey(ConsoleKey.Enter);
        testConsole.Input.PushTextWithEnter(mermaidFile);
        testConsole.Input.PushTextWithEnter(outputFile);

        var exportHandler = new ExportCommandHandler(testConsole);
        var wizard = new WizardCommandHandler(exportHandler: exportHandler, console: testConsole);

        var exitCode = await wizard.RunAsync();

        exitCode.Should().Be(0);
        File.Exists(outputFile).Should().BeTrue();
        testConsole.Output.Should().Contain("Successfully exported Data Dictionary");
    }
}

