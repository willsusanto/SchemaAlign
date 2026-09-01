using SchemaAlign.Appliers;
using SchemaAlign.Cli.Services;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

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

        if (action.StartsWith("3"))
        {
            var source = _console.Ask<string>("[bold]Enter schema path (e.g. schema.mmd, ./Entities, or script.sql):[/]");
            return await _inspectHandler.RunAsync(new InspectCommandOptions { Source = source }, cancellationToken);
        }

        var srcPath = _console.Ask<string>("[bold]Enter Source schema path (Desired state, e.g. schema.mmd):[/]");
        var tgtPath = _console.Ask<string>("[bold]Enter Target schema path (Existing state, e.g. ./src/Entities):[/]");

        var modeChoice = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]Select diff mode:[/]")
                .AddChoices("1. Incremental Mode (Recommended: Sprint diagram; preserves unmentioned tables)",
                            "2. Full Snapshot Mode (Source is exact full truth; marks omitted tables as deleted)"));

        var mode = modeChoice.StartsWith("1") ? "incremental" : "snapshot";

        if (action.StartsWith("1"))
        {
            var detailed = _console.Confirm("Show detailed property-level change tree?", defaultValue: true);
            return await _diffHandler.RunAsync(new DiffCommandOptions
            {
                Source = srcPath,
                Target = tgtPath,
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
                Source = srcPath,
                Target = tgtPath,
                Mode = mode,
                AllowDrop = allowDrops,
                DryRun = dryRun,
                Interactive = true,
                Yes = false
            }, cancellationToken);
        }

        return 0;
    }
}
