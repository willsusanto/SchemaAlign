using SchemaAlign.Appliers;
using SchemaAlign.Appliers.CSharp;
using SchemaAlign.Appliers.SqlServer;
using SchemaAlign.Cli.Rendering;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

/// <summary>
/// Options for configuring the schema sync command.
/// </summary>
public class SyncCommandOptions
{
    /// <summary>
    /// Path to current/base schema to be updated.
    /// </summary>
    public string Current { get; set; } = string.Empty;

    /// <summary>
    /// Path to desired/target schema to align toward.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit path to export generated migration script to disk (e.g. migration.sql).
    /// </summary>
    public string? OutputFile { get; set; }

    /// <summary>
    /// Diff mode ('incremental' or 'snapshot').
    /// </summary>
    public string Mode { get; set; } = "incremental";

    /// <summary>
    /// Whether destructive drops (DROP TABLE, DROP COLUMN) are permitted.
    /// </summary>
    public bool AllowDrop { get; set; } = false;

    /// <summary>
    /// Whether to run interactive checklist prompt to toggle individual changes.
    /// </summary>
    public bool Interactive { get; set; } = true;

    /// <summary>
    /// Whether to generate and preview diffs without writing modifications to disk.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Whether to apply changes non-interactively without confirmation prompt.
    /// </summary>
    public bool Yes { get; set; } = false;

    /// <summary>
    /// Optional override for the target schema type.
    /// </summary>
    public TargetType? TargetTypeOverride { get; set; }

    /// <summary>
    /// Target C# namespace for generated entities.
    /// </summary>
    public string? Namespace { get; set; }
}

/// <summary>
/// Command handler for synchronizing current base schema to match desired target schema.
/// </summary>
public class SyncCommandHandler
{
    private readonly ApplierRegistry _applierRegistry;
    private readonly SchemaDetectionService _detectionService;
    private readonly IAnsiConsole _console;

    public SyncCommandHandler(ApplierRegistry? applierRegistry = null, SchemaDetectionService? detectionService = null, IAnsiConsole? console = null)
    {
        _applierRegistry = applierRegistry ?? new ApplierRegistry();
        _detectionService = detectionService ?? new SchemaDetectionService();
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// Runs the sync command asynchronously with the provided options.
    /// </summary>
    /// <param name="options">Sync command options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code (0 for success, non-zero for error).</returns>
    public virtual async Task<int> RunAsync(SyncCommandOptions options, CancellationToken cancellationToken = default)
    {
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

            var targetType = options.TargetTypeOverride ?? _detectionService.DetectTargetType(options.Current);

            return await ExecuteAsync(currentSchema, targetSchema, options, targetType, cancellationToken);
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error executing sync:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    /// <summary>
    /// Executes the schema synchronization pipeline including diff calculation, interactive selection, preview, and application.
    /// </summary>
    /// <param name="currentSchema">Base schema.</param>
    /// <param name="targetSchema">Desired target schema.</param>
    /// <param name="options">Sync command options.</param>
    /// <param name="targetType">Detected or overridden target type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code (0 for success, non-zero for error).</returns>
    public async Task<int> ExecuteAsync(DatabaseSchema currentSchema, DatabaseSchema targetSchema, SyncCommandOptions options, TargetType targetType, CancellationToken cancellationToken = default)
    {
        var isSnapshot = string.Equals(options.Mode, "snapshot", StringComparison.OrdinalIgnoreCase);
        var diffOptions = isSnapshot ? SchemaDiffOptions.FullSnapshot : SchemaDiffOptions.Incremental;

        // Calculate diff: Current (base) -> Target (desired state)
        var diff = SchemaDiffCalculator.Calculate(currentSchema, targetSchema, diffOptions);

        if (!diff.HasChanges)
        {
            _console.MarkupLine("[green]✔ Current schema is already aligned with desired target. No changes needed.[/]");
            return 0;
        }

        // Print initial diff table
        DiffConsoleRenderer.RenderConsole(diff, _console, detailed: false);

        var hasDestructive = DiffFilter.HasDestructiveChanges(diff);
        if (hasDestructive)
        {
            _console.MarkupLine("\n[bold red]⚠️  Warning: Destructive schema changes (DROP TABLE or DROP COLUMN) were detected.[/]");
        }

        // Handle filtering / interactivity
        if (options.Interactive)
        {
            diff = InteractiveChangeSelector.PromptSelection(diff, _console, preselectDrops: options.AllowDrop);
            if (!diff.HasChanges)
            {
                _console.MarkupLine("[yellow]No changes selected. Aborting sync.[/]");
                return 0;
            }
        }
        else
        {
            if (hasDestructive && !options.AllowDrop)
            {
                _console.MarkupLine("[yellow]Non-interactive mode without --allow-drop: Automatically excluding destructive drops.[/]");
                diff = DiffFilter.Filter(diff, allowDrops: false);
            }
        }

        // Resolve Applier
        ISchemaApplier applier;
        try
        {
            applier = _applierRegistry.Resolve(targetType);
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Applier resolution error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        var currentPaths = options.Current.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var primaryTargetDir = currentPaths.Length > 0 ? currentPaths[0] : options.Current;

        ApplierOptions applierOptions;
        if (targetType == TargetType.CSharp)
        {
            applierOptions = new CSharpApplierOptions
            {
                TargetDirectory = primaryTargetDir,
                SourceDirectories = currentPaths.ToList(),
                DefaultNamespace = !string.IsNullOrWhiteSpace(options.Namespace) ? options.Namespace : "Entities",
                AutoDetectNamespace = string.IsNullOrWhiteSpace(options.Namespace),
                AllowDrops = options.AllowDrop,
                DryRun = options.DryRun
            };
        }
        else if (targetType == TargetType.SqlServerDatabase || targetType == TargetType.SqlServerScript)
        {
            applierOptions = new SqlServerApplierOptions
            {
                TargetDirectory = primaryTargetDir,
                AllowDrops = options.AllowDrop,
                DryRun = options.DryRun,
                ConnectionString = IsConnectionString(primaryTargetDir) ? primaryTargetDir : null,
                ScriptOutputFilePath = options.OutputFile
            };
        }
        else
        {
            applierOptions = new ApplierOptions
            {
                TargetDirectory = primaryTargetDir,
                AllowDrops = options.AllowDrop,
                DryRun = options.DryRun
            };
        }

        // Preview
        var previews = await applier.PreviewAsync(diff, applierOptions, cancellationToken);
        PreviewConsoleRenderer.RenderPreviews(previews, _console);

        if (options.DryRun)
        {
            _console.MarkupLine("\n[yellow]✔ Dry-run mode completed. No changes were applied.[/]");
            return 0;
        }

        // Confirmation & Branching
        if (targetType == TargetType.SqlServerDatabase && !options.Yes)
        {
            var sqlOpt = (SqlServerApplierOptions)applierOptions;
            if (!string.IsNullOrWhiteSpace(options.OutputFile))
            {
                var confirmed = _console.Confirm($"\n[bold]Export SQL Server migration script to '{options.OutputFile}'?[/]", defaultValue: true);
                if (!confirmed)
                {
                    _console.MarkupLine("[grey]Sync cancelled by user.[/]");
                    return 0;
                }
            }
            else
            {
                var actionChoice = _console.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\n[bold]Select how to apply these SQL Server changes:[/]")
                        .AddChoices(
                            "1. Output to a .sql transaction script file",
                            "2. Apply directly to the Live Database",
                            "3. Cancel"));

                if (actionChoice.StartsWith("1"))
                {
                    var outPath = _console.Prompt(new TextPrompt<string>("[bold]Enter output .sql file path (e.g. ./migration.sql):[/]").DefaultValue("migration.sql"));
                    sqlOpt.ScriptOutputFilePath = outPath;
                }
                else if (actionChoice.StartsWith("2"))
                {
                    var confirmed = _console.Confirm("\n[bold red]Are you sure you want to execute these changes directly against the live database?[/]", defaultValue: false);
                    if (!confirmed)
                    {
                        _console.MarkupLine("[grey]Sync cancelled by user.[/]");
                        return 0;
                    }
                }
                else
                {
                    _console.MarkupLine("[grey]Sync cancelled by user.[/]");
                    return 0;
                }
            }
        }
        else if (!options.Yes)
        {
            var targetLabel = targetType == TargetType.CSharp ? "C# codebase" : "current schema";
            var confirmed = _console.Confirm($"\n[bold]Apply these changes to {targetLabel}?[/]", defaultValue: false);
            if (!confirmed)
            {
                _console.MarkupLine("[grey]Sync cancelled by user.[/]");
                return 0;
            }
        }

        // Apply
        _console.MarkupLine("\n[bold blue]Applying changes to current schema...[/]");
        var result = await applier.ApplyAsync(diff, applierOptions, cancellationToken);

        if (result.Success)
        {
            if (applierOptions is SqlServerApplierOptions sqlOpt && !string.IsNullOrWhiteSpace(sqlOpt.ScriptOutputFilePath))
            {
                _console.MarkupLine($"[green]✔ Successfully exported SQL Server migration script to '{sqlOpt.ScriptOutputFilePath}'![/]");
            }
            else
            {
                _console.MarkupLine($"[green]✔ Successfully applied changes! ({result.CreatedFiles.Count} created, {result.ChangedFiles.Count} modified).[/]");
            }
            return 0;
        }

        _console.MarkupLine("[red]Errors occurred while applying changes:[/]");
        foreach (var err in result.Errors)
        {
            _console.MarkupLine($"[red]  - {Markup.Escape(err)}[/]");
        }
        return 1;
    }

    private static bool IsConnectionString(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        return str.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
               str.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase);
    }
}
