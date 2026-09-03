using SchemaAlign.Cli.Rendering;
using SchemaAlign.Cli.Services;
using SchemaAlign.Configuration;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

/// <summary>
/// Options for configuring the schema diff command.
/// </summary>
public class DiffCommandOptions
{
    /// <summary>
    /// Path to current/base schema.
    /// </summary>
    public string Current { get; set; } = string.Empty;

    /// <summary>
    /// Path to desired/target schema.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Diff mode ('incremental' or 'snapshot').
    /// </summary>
    public string Mode { get; set; } = "incremental";

    /// <summary>
    /// Output format ('console', 'json', or 'markdown').
    /// </summary>
    public string Output { get; set; } = "console";

    /// <summary>
    /// Whether to display a detailed property-level change tree.
    /// </summary>
    public bool Detailed { get; set; } = false;

    /// <summary>
    /// Optional path to .schemaalign.json configuration file.
    /// </summary>
    public string? ConfigFile { get; set; }
}

/// <summary>
/// Command handler for comparing base and target schemas non-destructively.
/// </summary>
public class DiffCommandHandler
{
    private readonly SchemaDetectionService _detectionService;
    private readonly IAnsiConsole _console;

    public DiffCommandHandler(SchemaDetectionService? detectionService = null, IAnsiConsole? console = null)
    {
        _detectionService = detectionService ?? new SchemaDetectionService();
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// Runs the diff command asynchronously with the provided options.
    /// </summary>
    /// <param name="options">Diff command options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code (0 for success, non-zero for error).</returns>
    public virtual async Task<int> RunAsync(DiffCommandOptions options, CancellationToken cancellationToken = default)
    {
        // Load config if specified or find in current directory
        var config = !string.IsNullOrWhiteSpace(options.ConfigFile)
            ? await ConfigurationLoader.LoadAsync(options.ConfigFile, cancellationToken)
            : await ConfigurationLoader.FindAndLoadAsync(cancellationToken: cancellationToken);

        if (config != null)
        {
            if (string.IsNullOrWhiteSpace(options.Current) && !string.IsNullOrWhiteSpace(config.Current ?? config.Source))
                options.Current = (config.Current ?? config.Source)!;
            if (string.IsNullOrWhiteSpace(options.Target) && !string.IsNullOrWhiteSpace(config.Target))
                options.Target = config.Target;
            if (string.Equals(options.Mode, "incremental", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.Mode))
                options.Mode = config.Mode;
            if (string.Equals(options.Output, "console", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.Output))
                options.Output = config.Output;
            if (!options.Detailed && config.Detailed.HasValue)
                options.Detailed = config.Detailed.Value;
        }

        if (string.IsNullOrWhiteSpace(options.Current))
        {
            _console.MarkupLine("[red]Error: Current schema path (-c|--current) is required.[/]");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(options.Target))
        {
            _console.MarkupLine("[red]Error: Target schema path (-t|--target) is required.[/]");
            return 1;
        }

        try
        {
            var currentSchema = await _detectionService.ReadSchemaAsync(options.Current, cancellationToken);
            var targetSchema = await _detectionService.ReadSchemaAsync(options.Target, cancellationToken);

            return Execute(currentSchema, targetSchema, options);
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error executing diff:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    /// <summary>
    /// Calculates the schema diff and renders the output according to the specified options.
    /// </summary>
    /// <param name="sourceSchema">Base schema.</param>
    /// <param name="targetSchema">Desired target schema.</param>
    /// <param name="options">Diff options.</param>
    /// <returns>Exit code (0 for success).</returns>
    public int Execute(DatabaseSchema sourceSchema, DatabaseSchema targetSchema, DiffCommandOptions options)
    {
        var isSnapshot = string.Equals(options.Mode, "snapshot", StringComparison.OrdinalIgnoreCase);
        var diffOptions = isSnapshot ? SchemaDiffOptions.FullSnapshot : SchemaDiffOptions.Incremental;

        // Diff is calculated from source (current base) to target (desired state)
        var diff = SchemaDiffCalculator.Calculate(sourceSchema, targetSchema, diffOptions);

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
