using SchemaAlign.Appliers;
using SchemaAlign.Configuration;
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
    private readonly string? _configFile;

    public WizardCommandHandler(
        DiffCommandHandler? diffHandler = null,
        SyncCommandHandler? syncHandler = null,
        InspectCommandHandler? inspectHandler = null,
        IAnsiConsole? console = null,
        string? configFile = null)
    {
        _console = console ?? AnsiConsole.Console;
        _configFile = configFile;
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

        try
        {
            var configPath = _configFile ?? ConfigurationLoader.FindConfigFile();
            var config = configPath == null ? null : await ConfigurationLoader.LoadAsync(configPath, cancellationToken);
            _console.MarkupLine($"[grey]Relative paths start from: {Markup.Escape(Directory.GetCurrentDirectory())}[/]");

            if (action.StartsWith("3"))
            {
                var current = AskSchemaInput("[bold]Enter schema path (e.g. schema.mmd, ./Entities, or script.sql):[/]", config?.Current);
                return await _inspectHandler.RunAsync(new InspectCommandOptions { Current = current, TablePrefixes = config?.TablePrefixes.ToList() ?? new() }, cancellationToken);
            }

            var currentPath = AskSchemaInput("[bold]Enter Current schema path (e.g. ./src/Entities or live DB):[/]", config?.Current);
            var targetPath = AskSchemaInput("[bold]Enter Desired target schema path (e.g. schema.mmd or new spec):[/]", config?.Target);

            var modeChoice = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]What does your desired schema describe?[/]")
                    .AddChoices("1. This sprint's tables (Incremental): preserve other tables; include every desired column in each listed table",
                                "2. The complete system (Snapshot): report omitted tables, columns, and relationships for removal"));

            var mode = modeChoice.StartsWith("1") ? "incremental" : "snapshot";
            _console.MarkupLine("[grey]Incremental preserves omitted tables and foreign keys; omitted columns in included tables are reported for removal.[/]");

            if (action.StartsWith("1"))
            {
                return await _diffHandler.RunAsync(new DiffCommandOptions
                {
                    Current = currentPath,
                    Target = targetPath,
                    Mode = mode,
                    Detailed = true,
                    ConfigFile = configPath
                }, cancellationToken);
            }

            if (action.StartsWith("2"))
            {

                return await _syncHandler.RunAsync(new SyncCommandOptions
                {
                    Current = currentPath,
                    Target = targetPath,
                    Mode = mode,
                    ConfigFile = configPath,
                    Guided = true,

                    Interactive = true,
                    Yes = false
                }, cancellationToken);
            }

            return 0;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            _console.MarkupLine($"[red]Configuration or input error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private string AskSchemaInput(string prompt, string? configured = null)
    {
        while (true)
        {
            var input = configured ?? _console.Ask<string>(prompt);
            configured = null;
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
