using SchemaAlign.Cli.Rendering;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

public class DiffCommandOptions
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Mode { get; set; } = "incremental";
    public string Output { get; set; } = "console";
    public bool Detailed { get; set; } = false;
}

public class DiffCommandHandler
{
    private readonly SchemaDetectionService _detectionService;
    private readonly IAnsiConsole _console;

    public DiffCommandHandler(SchemaDetectionService? detectionService = null, IAnsiConsole? console = null)
    {
        _detectionService = detectionService ?? new SchemaDetectionService();
        _console = console ?? AnsiConsole.Console;
    }

    public virtual async Task<int> RunAsync(DiffCommandOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Source))
        {
            _console.MarkupLine("[red]Error: Source path (-s|--source) is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Target))
        {
            _console.MarkupLine("[red]Error: Target path (-t|--target) is required.[/]");
            return 1;
        }

        try
        {
            var sourceSchema = await _detectionService.ReadSchemaAsync(options.Source, cancellationToken);
            var targetSchema = await _detectionService.ReadSchemaAsync(options.Target, cancellationToken);

            return Execute(sourceSchema, targetSchema, options);
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error executing diff:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    public int Execute(DatabaseSchema sourceSchema, DatabaseSchema targetSchema, DiffCommandOptions options)
    {
        var isSnapshot = string.Equals(options.Mode, "snapshot", StringComparison.OrdinalIgnoreCase);
        var diffOptions = isSnapshot ? SchemaDiffOptions.FullSnapshot : SchemaDiffOptions.Incremental;

        // Diff is calculated from target (base) to source (desired)
        var diff = SchemaDiffCalculator.Calculate(targetSchema, sourceSchema, diffOptions);

        switch (options.Output.ToLowerInvariant())
        {
            case "json":
                _console.WriteLine(DiffConsoleRenderer.RenderJson(diff));
                break;
            case "markdown":
            case "md":
                _console.WriteLine(DiffConsoleRenderer.RenderMarkdown(diff));
                break;
            case "console":
            default:
                DiffConsoleRenderer.RenderConsole(diff, _console, options.Detailed);
                break;
        }

        return 0;
    }
}
