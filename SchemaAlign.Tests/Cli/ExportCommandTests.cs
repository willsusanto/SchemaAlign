using SchemaAlign.Cli;
using SchemaAlign.Cli.Commands;
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
        var exitCode = await rootCommand.Parse($"export --source \"{nonMermaidFile}\" --output \"{outputFile}\"").InvokeAsync();

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
        var exitCode = await rootCommand.Parse($"export -s \"{mermaidFile}\" -o \"{outputFile}\" --aid 5555 --db TEST_DB").InvokeAsync();

        exitCode.Should().Be(0);
        File.Exists(outputFile).Should().BeTrue();
    }
}
