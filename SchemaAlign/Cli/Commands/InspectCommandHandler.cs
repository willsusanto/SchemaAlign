using System.Text.Json;
using SchemaAlign.Cli.Services;
using SchemaAlign.Models;
using Spectre.Console;

namespace SchemaAlign.Cli.Commands;

/// <summary>
/// Options for configuring the schema inspect command.
/// </summary>
public class InspectCommandOptions
{
    /// <summary>
    /// Path to schema file or directory to inspect.
    /// </summary>
    public string Current { get; set; } = string.Empty;

    /// <summary>
    /// Output format ('console' or 'json').
    /// </summary>
    public string Output { get; set; } = "console";
}

/// <summary>
/// Command handler for inspecting and displaying parsed schema tables and columns.
/// </summary>
public class InspectCommandHandler
{
    private readonly SchemaDetectionService _detectionService;
    private readonly IAnsiConsole _console;

    public InspectCommandHandler(SchemaDetectionService? detectionService = null, IAnsiConsole? console = null)
    {
        _detectionService = detectionService ?? new SchemaDetectionService();
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// Runs the inspect command asynchronously with the provided options.
    /// </summary>
    /// <param name="options">Inspect command options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Exit code (0 for success, non-zero for error).</returns>
    public virtual async Task<int> RunAsync(InspectCommandOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.Current))
        {
            _console.MarkupLine("[red]Error: Current path (-c|--current) is required.[/]");
            return 1;
        }

        try
        {
            var schema = await _detectionService.ReadSchemaAsync(options.Current, cancellationToken);

            switch (options.Output.ToLowerInvariant())
            {
                case "json":
                    var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
                    _console.WriteLine(json);
                    break;
                case "console":
                default:
                    RenderConsole(schema, options.Current);
                    break;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error inspecting schema:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private void RenderConsole(DatabaseSchema schema, string sourcePath)
    {
        var root = new Tree($"[bold blue]Schema: {Markup.Escape(sourcePath)} ({schema.Tables.Count} tables)[/]");

        foreach (var table in schema.Tables.Values)
        {
            var tableNode = root.AddNode($"[bold]{Markup.Escape(table.Schema)}.{Markup.Escape(table.Name)}[/]");

            var colGroup = tableNode.AddNode("[grey]Columns:[/]");
            foreach (var col in table.Columns.Values)
            {
                var pkBadge = col.IsPrimaryKey ? " [yellow](PK)[/]" : "";
                var identityBadge = col.IsIdentity ? " [cyan](Identity)[/]" : "";
                var nullableBadge = col.IsNullable ? "?" : "";
                var lengthBadge = col.Length.HasValue ? $"({col.Length})" : "";
                colGroup.AddNode($"{Markup.Escape(col.Name)}: [green]{col.Type}{lengthBadge}{nullableBadge}[/]{pkBadge}{identityBadge}");
            }

            if (table.ForeignKeys.Count > 0)
            {
                var fkGroup = tableNode.AddNode("[grey]Foreign Keys:[/]");
                foreach (var fk in table.ForeignKeys)
                {
                    var name = fk.ConstraintName ?? "(unnamed)";
                    fkGroup.AddNode($"[blue]{Markup.Escape(name)}[/]: {Markup.Escape(fk.PrincipalTable)}.{Markup.Escape(fk.PrincipalColumn)} ({fk.Cardinality})");
                }
            }
        }

        _console.Write(root);
    }
}
