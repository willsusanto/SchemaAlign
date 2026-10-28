using SchemaAlign.Appliers;
using SchemaAlign.Cli.Services;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

/// <summary>
/// Command handler for running the interactive CLI wizard when no subcommands are supplied.
/// </summary>
public class WizardCommandHandler
{
    private readonly DiffCommandHandler _diffHandler;
    private readonly SyncCommandHandler _syncHandler;
    private readonly InspectCommandHandler _inspectHandler;
    private readonly IAnsiConsole _console;

    public WizardCommandHandler(
        DiffCommandHandler? diffHandler = null,
        SyncCommandHandler? syncHandler = null,
        InspectCommandHandler? inspectHandler = null,
        IAnsiConsole? console = null)
    {
        _console = console ?? AnsiConsole.Console;
        var detection = new SchemaDetectionService();
        var registry = new ApplierRegistry();

        _diffHandler = diffHandler ?? new DiffCommandHandler(detection, _console);
        _syncHandler = syncHandler ?? new SyncCommandHandler(registry, detection, _console);
        _inspectHandler = inspectHandler ?? new InspectCommandHandler(detection, _console);
    }

    /// <summary>
    /// Runs the interactive console wizard workflow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code (0 for success, non-zero for error).</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _console.Write(new FigletText("SchemaAlign").Color(Color.Cyan1));
        _console.MarkupLine("[bold blue]Universal Database & Entity Schema Alignment Tool[/]\n");

        var action = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]What would you like to do?[/]")
                .AddChoices("1. Diff Schemas (Compare without modifying)",
                            "2. Sync Target (Review, preview, and apply changes)",
                            "3. Inspect Schema (View parsed tables & columns)",
                            "4. Exit"));

        if (action.StartsWith("4"))
            return 0;

        _console.MarkupLine($"[grey]Relative paths start from: {Markup.Escape(Directory.GetCurrentDirectory())}[/]");

        if (action.StartsWith("3"))
        {
            var current = AskSchemaInput("[bold]Enter schema path (e.g. schema.mmd, ./Entities, or script.sql):[/]");
            return await _inspectHandler.RunAsync(new InspectCommandOptions { Current = current }, cancellationToken);
        }

        var currentPath = AskSchemaInput("[bold]Enter Current schema path (e.g. ./src/Entities or live DB):[/]");
        var targetPath = AskSchemaInput("[bold]Enter Desired target schema path (e.g. schema.mmd or new spec):[/]");

        var modeChoice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Select diff mode:[/]")
                .AddChoices("1. Incremental Mode (Recommended: Sprint diagram; preserves unmentioned tables in current codebase)",
                            "2. Full Snapshot Mode (Desired schema is exact full truth; marks tables omitted from desired spec as deleted)"));

        var mode = modeChoice.StartsWith("1") ? "incremental" : "snapshot";

        if (action.StartsWith("1"))
        {
            var detailed = _console.Confirm("Show detailed property-level change tree?", defaultValue: true);
            return await _diffHandler.RunAsync(new DiffCommandOptions
            {
                Current = currentPath,
                Target = targetPath,
                Mode = mode,
                Detailed = detailed
            }, cancellationToken);
        }

        if (action.StartsWith("2"))
        {
            var allowDrops = _console.Confirm("Allow destructive drops (DROP TABLE / DROP COLUMN) if selected in checklist?", defaultValue: false);
            var dryRun = _console.Confirm("Run in Dry-Run preview mode first?", defaultValue: false);

            return await _syncHandler.RunAsync(new SyncCommandOptions
            {
                Current = currentPath,
                Target = targetPath,
                Mode = mode,
                AllowDrop = allowDrops,
                DryRun = dryRun,
                Interactive = true,
                Yes = false
            }, cancellationToken);
        }

        return 0;
    }

    private string AskSchemaInput(string prompt)
    {
        while (true)
        {
            var input = _console.Ask<string>(prompt);
            try
            {
                return SchemaInputValidator.ValidateAndNormalize(input);
            }
            catch (ArgumentException ex)
            {
                _console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            }
        }
    }
}
