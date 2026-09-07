using System.Text;
using System.Text.Json;
using SchemaAlign.Appliers;
using SchemaAlign.Diff;
using Spectre.Console;

namespace SchemaAlign.Cli.Rendering;

/// <summary>
/// Renders schema diff summaries and detailed change trees to Spectre.Console, Markdown, or JSON.
/// </summary>
public static class DiffConsoleRenderer
{
    /// <summary>
    /// Renders a colorized overview panel, summary table, and optional detailed change tree to the console.
    /// </summary>
    /// <param name="diff">The schema diff to render.</param>
    /// <param name="console">The AnsiConsole instance.</param>
    /// <param name="detailed">Whether to render the detailed change tree.</param>
    /// <param name="maxTables">Maximum number of tables to display in the console before truncating.</param>
    /// <param name="reportPath">Optional path to the exported markdown report for truncation notice.</param>
    public static void RenderConsole(SchemaDiff diff, IAnsiConsole console, bool detailed = false, int maxTables = 15, string? reportPath = null)
    {
        if (!diff.HasChanges)
        {
            console.MarkupLine("[green]✔ Schemas are identical. No differences found.[/]");
            return;
        }

        // 1. KPI Panel
        var addedCount = diff.AddedTables.Count();
        var modCount = diff.ModifiedTables.Count();
        var delCount = diff.DeletedTables.Count();

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(
            $"[green]+ {addedCount} Added[/]",
            $"[yellow]~ {modCount} Modified[/]",
            $"[red]- {delCount} Deleted[/]"
        );

        console.Write(new Panel(grid)
        {
            Header = new PanelHeader("[bold blue]Schema Diff Overview[/]"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        });

        // 2. Summary Table
        var changedTables = diff.Tables.Where(x => x.HasChanges).ToList();
        var displayTables = maxTables > 0 && changedTables.Count > maxTables
            ? changedTables.Take(maxTables).ToList()
            : changedTables;

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Status[/]");
        table.AddColumn("[bold]Table[/]");
        table.AddColumn("[bold]Schema[/]");
        table.AddColumn("[bold]Columns[/]");
        table.AddColumn("[bold]Foreign Keys[/]");

        foreach (var t in displayTables)
        {
            var status = t.Kind switch
            {
                DiffKind.Added => "[green]+ ADDED[/]",
                DiffKind.Modified => "[yellow]~ MODIFIED[/]",
                DiffKind.Deleted => "[red]- DELETED[/]",
                _ => "[grey]UNCHANGED[/]"
            };

            var colSummary = t.Kind switch
            {
                DiffKind.Added => $"[green]+{t.AddedColumns.Count()}[/]",
                DiffKind.Deleted => $"[red]-{t.DeletedColumns.Count()}[/]",
                DiffKind.Modified => $"[green]+{t.AddedColumns.Count()}[/], [yellow]~{t.ModifiedColumns.Count()}[/], [red]-{t.DeletedColumns.Count()}[/]",
                _ => $"{t.Columns.Count}"
            };

            var fkSummary = t.Kind switch
            {
                DiffKind.Added => $"[green]+{t.AddedForeignKeys.Count()}[/]",
                DiffKind.Deleted => $"[red]-{t.DeletedForeignKeys.Count()}[/]",
                DiffKind.Modified => $"[green]+{t.AddedForeignKeys.Count()}[/], [yellow]~{t.ModifiedForeignKeys.Count()}[/], [red]-{t.DeletedForeignKeys.Count()}[/]",
                _ => $"{t.ForeignKeys.Count}"
            };

            table.AddRow(status, Markup.Escape(t.TableName), Markup.Escape(t.Schema), colSummary, fkSummary);
        }

        console.Write(table);

        if (changedTables.Count > displayTables.Count)
        {
            var remaining = changedTables.Count - displayTables.Count;
            var reportNotice = !string.IsNullOrWhiteSpace(reportPath)
                ? $" [blue]Full details in:[/] [underline]{Markup.Escape(reportPath)}[/]"
                : " (Specify --report to export full details)";
            console.MarkupLine($"[grey]... and {remaining} more changed tables ({changedTables.Count} total).[/]{reportNotice}");
        }

        // Legend
        console.MarkupLine("[grey]Legend:[/] [green]+ Added[/] (in target, missing in source) | [yellow]~ Modified[/] (attribute/type differs) | [red]- Deleted[/] (in source, missing in target)");

        // 3. Detailed Tree View
        if (detailed)
        {
            RenderDetailedTree(diff, console, maxTables, reportPath);
        }
    }

    /// <summary>
    /// Renders a detailed property-level change tree to the console.
    /// </summary>
    /// <param name="diff">The schema diff to render.</param>
    /// <param name="console">The AnsiConsole instance.</param>
    /// <param name="maxTables">Maximum number of tables to include before truncating.</param>
    /// <param name="reportPath">Optional path to the exported markdown report for truncation notice.</param>
    public static void RenderDetailedTree(SchemaDiff diff, IAnsiConsole console, int maxTables = 15, string? reportPath = null)
    {
        var root = new Tree("[bold underline]Detailed Changes[/] [grey]([green]+ Added[/] | [yellow]~ Modified[/] | [red]- Deleted[/])[/]");
        var changedTables = diff.Tables.Where(x => x.HasChanges).ToList();
        var displayTables = maxTables > 0 && changedTables.Count > maxTables
            ? changedTables.Take(maxTables).ToList()
            : changedTables;

        foreach (var t in displayTables)
        {
            var tableNode = t.Kind switch
            {
                DiffKind.Added => root.AddNode($"[green]+ Table: {Markup.Escape(t.TableName)} (New)[/]"),
                DiffKind.Deleted => root.AddNode($"[red]- Table: {Markup.Escape(t.TableName)} (Deleted)[/]"),
                _ => root.AddNode($"[yellow]~ Table: {Markup.Escape(t.TableName)}[/]")
            };

            if (t.Columns.Any(c => c.Kind != DiffKind.Unchanged))
            {
                var colGroup = tableNode.AddNode("[bold]Columns:[/]");
                foreach (var col in t.Columns.Where(c => c.Kind != DiffKind.Unchanged))
                {
                    switch (col.Kind)
                    {
                        case DiffKind.Added:
                            var addType = col.Target != null ? $"{col.Target.Type}{(col.Target.Length.HasValue ? $"({col.Target.Length})" : "")}{(col.Target.IsNullable ? "?" : "")}" : "";
                            colGroup.AddNode($"[green]+ {Markup.Escape(col.ColumnName)}: {addType}[/]");
                            break;
                        case DiffKind.Deleted:
                            colGroup.AddNode($"[red]- {Markup.Escape(col.ColumnName)} (Drop)[/]");
                            break;
                        case DiffKind.Modified:
                            var srcType = col.Source != null ? $"{col.Source.Type}{(col.Source.Length.HasValue ? $"({col.Source.Length})" : "")}{(col.Source.IsNullable ? "?" : "")}" : "";
                            var tgtType = col.Target != null ? $"{col.Target.Type}{(col.Target.Length.HasValue ? $"({col.Target.Length})" : "")}{(col.Target.IsNullable ? "?" : "")}" : "";
                            colGroup.AddNode($"[yellow]~ {Markup.Escape(col.ColumnName)}: {srcType} -> {tgtType} ({col.Changes})[/]");
                            break;
                    }
                }
            }

            if (t.ForeignKeys.Any(f => f.Kind != DiffKind.Unchanged))
            {
                var fkGroup = tableNode.AddNode("[bold]Foreign Keys:[/]");
                foreach (var fk in t.ForeignKeys.Where(f => f.Kind != DiffKind.Unchanged))
                {
                    var name = fk.ConstraintName ?? "(unnamed)";
                    switch (fk.Kind)
                    {
                        case DiffKind.Added:
                            fkGroup.AddNode($"[green]+ {Markup.Escape(name)}[/]");
                            break;
                        case DiffKind.Deleted:
                            fkGroup.AddNode($"[red]- {Markup.Escape(name)} (Drop)[/]");
                            break;
                        case DiffKind.Modified:
                            fkGroup.AddNode($"[yellow]~ {Markup.Escape(name)} (Cardinality modified)[/]");
                            break;
                    }
                }
            }
        }

        if (changedTables.Count > displayTables.Count)
        {
            var remaining = changedTables.Count - displayTables.Count;
            var reportNotice = !string.IsNullOrWhiteSpace(reportPath)
                ? $" Full details in {reportPath}."
                : "";
            root.AddNode($"[grey]... and {remaining} more tables.{reportNotice}[/]");
        }

        console.Write(root);
    }

    /// <summary>
    /// Renders a comprehensive schema diff and alignment summary formatted as Markdown.
    /// </summary>
    /// <param name="diff">The schema diff to render.</param>
    /// <param name="previews">Optional file diff previews to embed in the markdown report.</param>
    /// <param name="title">Optional custom document title.</param>
    /// <returns>A markdown formatted string.</returns>
    public static string RenderMarkdown(SchemaDiff diff, IReadOnlyList<FileDiffPreview>? previews = null, string? title = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title ?? "Schema Diff Summary"}");
        sb.AppendLine();
        sb.AppendLine($"- **HasChanges**: `{diff.HasChanges}`");
        sb.AppendLine($"- **Added Tables**: {diff.AddedTables.Count()}");
        sb.AppendLine($"- **Modified Tables**: {diff.ModifiedTables.Count()}");
        sb.AppendLine($"- **Deleted Tables**: {diff.DeletedTables.Count()}");
        sb.AppendLine();

        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine("| Status | Table | Schema | Added Cols | Mod Cols | Del Cols | Added FKs | Mod FKs | Del FKs |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

        foreach (var t in diff.Tables.Where(x => x.HasChanges))
        {
            sb.AppendLine($"| {t.Kind} | `{t.TableName}` | `{t.Schema}` | {t.AddedColumns.Count()} | {t.ModifiedColumns.Count()} | {t.DeletedColumns.Count()} | {t.AddedForeignKeys.Count()} | {t.ModifiedForeignKeys.Count()} | {t.DeletedForeignKeys.Count()} |");
        }

        var changedTables = diff.Tables.Where(x => x.HasChanges).ToList();
        if (changedTables.Any(t => t.Columns.Any(c => c.Kind != DiffKind.Unchanged) || t.ForeignKeys.Any(f => f.Kind != DiffKind.Unchanged)))
        {
            sb.AppendLine();
            sb.AppendLine("## Detailed Changes");

            foreach (var t in changedTables)
            {
                sb.AppendLine();
                sb.AppendLine($"### `{t.TableName}` ({t.Kind})");

                if (t.Columns.Any(c => c.Kind != DiffKind.Unchanged))
                {
                    sb.AppendLine();
                    sb.AppendLine("**Columns:**");
                    foreach (var c in t.Columns.Where(c => c.Kind != DiffKind.Unchanged))
                    {
                        switch (c.Kind)
                        {
                            case DiffKind.Added:
                                var addType = c.Target != null ? $"{c.Target.Type}{(c.Target.Length.HasValue ? $"({c.Target.Length})" : "")}{(c.Target.IsNullable ? "?" : "")}" : "";
                                sb.AppendLine($"- **+ ADD** `{c.ColumnName}` ({addType})");
                                break;
                            case DiffKind.Deleted:
                                sb.AppendLine($"- **- DROP** `{c.ColumnName}` (DESTRUCTIVE)");
                                break;
                            case DiffKind.Modified:
                                var srcType = c.Source != null ? $"{c.Source.Type}{(c.Source.Length.HasValue ? $"({c.Source.Length})" : "")}{(c.Source.IsNullable ? "?" : "")}" : "";
                                var tgtType = c.Target != null ? $"{c.Target.Type}{(c.Target.Length.HasValue ? $"({c.Target.Length})" : "")}{(c.Target.IsNullable ? "?" : "")}" : "";
                                sb.AppendLine($"- **~ MODIFY** `{c.ColumnName}`: `{srcType}` -> `{tgtType}` ({c.Changes})");
                                break;
                        }
                    }
                }

                if (t.ForeignKeys.Any(f => f.Kind != DiffKind.Unchanged))
                {
                    sb.AppendLine();
                    sb.AppendLine("**Foreign Keys:**");
                    foreach (var f in t.ForeignKeys.Where(f => f.Kind != DiffKind.Unchanged))
                    {
                        var fkName = f.ConstraintName ?? "(unnamed)";
                        switch (f.Kind)
                        {
                            case DiffKind.Added:
                                var addRef = f.Target != null ? $"{f.Target.DependentTable}.{f.Target.DependentColumn} -> {f.Target.PrincipalTable}.{f.Target.PrincipalColumn}" : "";
                                sb.AppendLine($"- **+ ADD FK** `{fkName}` ({addRef})");
                                break;
                            case DiffKind.Deleted:
                                sb.AppendLine($"- **- DROP FK** `{fkName}` (DESTRUCTIVE)");
                                break;
                            case DiffKind.Modified:
                                sb.AppendLine($"- **~ MODIFY FK** `{fkName}` (Cardinality/Target modified)");
                                break;
                        }
                    }
                }
            }
        }

        if (previews != null && previews.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## File Previews");
            foreach (var p in previews)
            {
                sb.AppendLine();
                sb.AppendLine($"### {p.DiffKind}: `{p.FilePath}`");
                sb.AppendLine();
                sb.AppendLine("```diff");
                sb.AppendLine(p.UnifiedDiff ?? "(No textual diff)");
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the schema diff summary formatted as indented JSON.
    /// </summary>
    /// <param name="diff">The schema diff to render.</param>
    /// <returns>A JSON string representation of the diff.</returns>
    public static string RenderJson(SchemaDiff diff)
    {
        var payload = new
        {
            hasChanges = diff.HasChanges,
            summary = new
            {
                addedTables = diff.AddedTables.Count(),
                modifiedTables = diff.ModifiedTables.Count(),
                deletedTables = diff.DeletedTables.Count()
            },
            tables = diff.Tables.Where(t => t.HasChanges).Select(t => new
            {
                tableName = t.TableName,
                schema = t.Schema,
                kind = t.Kind.ToString(),
                columns = t.Columns.Where(c => c.Kind != DiffKind.Unchanged).Select(c => new
                {
                    columnName = c.ColumnName,
                    kind = c.Kind.ToString(),
                    changes = c.Changes.ToString()
                }).ToList(),
                foreignKeys = t.ForeignKeys.Where(f => f.Kind != DiffKind.Unchanged).Select(f => new
                {
                    constraintName = f.ConstraintName,
                    kind = f.Kind.ToString(),
                    cardinalityChanged = f.CardinalityChanged
                }).ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }
}
