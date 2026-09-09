using SchemaAlign.Exporters.Excel;
using SchemaAlign.Readers.Mermaid;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

/// <summary>
/// Options for configuring the export command.
/// </summary>
public class ExportCommandOptions
{
    public string Target { get; set; } = string.Empty;
    public string Output { get; set; } = string.Empty;
    public string? Config { get; set; }
    public string? Aid { get; set; }
    public string? Ip { get; set; }
    public string? Db { get; set; }
    public string? Title { get; set; }
}

/// <summary>
/// Command handler for exporting a Mermaid ER diagram to an Excel data dictionary.
/// </summary>
public class ExportCommandHandler
{
    private readonly IAnsiConsole _console;

    public ExportCommandHandler(IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// Runs the export command asynchronously.
    /// </summary>
    /// <param name="options">Export options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code (0 for success, non-zero for error).</returns>
    public virtual async Task<int> RunAsync(ExportCommandOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Target))
        {
            _console.MarkupLine("[red]Error: Target path (-t|--target) is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Output))
        {
            _console.MarkupLine("[red]Error: Output path (-o|--output) is required.[/]");
            return 1;
        }

        if (!File.Exists(options.Target))
        {
            _console.MarkupLine($"[red]Error: Target file not found: '{Markup.Escape(options.Target)}'[/]");
            return 1;
        }

        var ext = Path.GetExtension(options.Target);
        if (!ext.Equals(".mmd", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".mermaid", StringComparison.OrdinalIgnoreCase))
        {
            _console.MarkupLine("[red]Error: Only Mermaid ER diagrams (.mmd, .mermaid) are currently supported as export target.[/]");
            return 1;
        }

        var outputPath = options.Output;
        if (!outputPath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            outputPath += ".xlsx";
        }

        try
        {
            var content = await File.ReadAllTextAsync(options.Target, cancellationToken);
            var reader = new MermaidSchemaReader();
            var schema = reader.Read(content);

            var exportOptions = DictionaryConfigLoader.Load(
                options.Config,
                options.Aid,
                options.Ip,
                options.Db,
                options.Title,
                options.Target,
                outputPath);

            var exporter = new ExcelDataDictionaryExporter();
            exporter.Export(schema, exportOptions);

            var totalCols = schema.Tables.Values.Sum(t => t.Columns.Count);
            _console.MarkupLine($"[green]Successfully exported Data Dictionary to:[/] [bold]{Markup.Escape(outputPath)}[/]");
            _console.MarkupLine($"[grey]Total Tables: {schema.Tables.Count} | Total Columns: {totalCols} | Database: {Markup.Escape(exportOptions.DatabaseName)}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error exporting data dictionary:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
