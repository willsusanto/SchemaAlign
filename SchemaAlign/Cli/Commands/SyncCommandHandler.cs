using SchemaAlign.Appliers;
using SchemaAlign.Cli.Rendering;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

public class SyncCommandOptions
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Mode { get; set; } = "incremental";
    public bool AllowDrop { get; set; } = false;
    public bool Interactive { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public bool Yes { get; set; } = false;
    public TargetType? TargetTypeOverride { get; set; }
}

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

    public virtual async Task<int> RunAsync(SyncCommandOptions options, CancellationToken cancellationToken = default)
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

            var targetType = options.TargetTypeOverride ?? _detectionService.DetectTargetType(options.Target);

            return await ExecuteAsync(sourceSchema, targetSchema, options, targetType, cancellationToken);
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error executing sync:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    public async Task<int> ExecuteAsync(DatabaseSchema sourceSchema, DatabaseSchema targetSchema, SyncCommandOptions options, TargetType targetType, CancellationToken cancellationToken = default)
    {
        var isSnapshot = string.Equals(options.Mode, "snapshot", StringComparison.OrdinalIgnoreCase);
        var diffOptions = isSnapshot ? SchemaDiffOptions.FullSnapshot : SchemaDiffOptions.Incremental;

        // Calculate diff: Target (current) -> Source (desired)
        var diff = SchemaDiffCalculator.Calculate(targetSchema, sourceSchema, diffOptions);

        if (!diff.HasChanges)
        {
            _console.MarkupLine("[green]✔ Target is already aligned with source. No changes needed.[/]");
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

        var applierOptions = new ApplierOptions
        {
            TargetDirectory = options.Target,
            AllowDrops = options.AllowDrop,
            DryRun = options.DryRun
        };

        // Preview
        var previews = await applier.PreviewAsync(diff, applierOptions, cancellationToken);
        PreviewConsoleRenderer.RenderPreviews(previews, _console);

        if (options.DryRun)
        {
            _console.MarkupLine("\n[yellow]✔ Dry-run mode completed. No changes were applied.[/]");
            return 0;
        }

        // Confirm
        if (!options.Yes)
        {
            var confirmed = _console.Confirm("\n[bold]Apply these changes to target?[/]", defaultValue: false);
            if (!confirmed)
            {
                _console.MarkupLine("[grey]Sync cancelled by user.[/]");
                return 0;
            }
        }

        // Apply
        _console.MarkupLine("\n[bold blue]Applying changes to target...[/]");
        var result = await applier.ApplyAsync(diff, applierOptions, cancellationToken);

        if (result.Success)
        {
            _console.MarkupLine($"[green]✔ Successfully applied changes! ({result.CreatedFiles.Count} created, {result.ChangedFiles.Count} modified).[/]");
            return 0;
        }

        _console.MarkupLine("[red]Errors occurred while applying changes:[/]");
        foreach (var err in result.Errors)
        {
            _console.MarkupLine($"[red]  - {Markup.Escape(err)}[/]");
        }
        return 1;
    }
}
