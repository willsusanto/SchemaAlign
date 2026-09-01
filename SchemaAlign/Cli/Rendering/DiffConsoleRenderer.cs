using System.Text;
using System.Text.Json;
using SchemaAlign.Diff;
using Spectre.Console;

namespace SchemaAlign.Cli.Rendering;

public static class DiffConsoleRenderer
{
    public static void RenderConsole(SchemaDiff diff, IAnsiConsole console, bool detailed = false)
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
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Status[/]");
        table.AddColumn("[bold]Table[/]");
        table.AddColumn("[bold]Schema[/]");
        table.AddColumn("[bold]Columns[/]");
        table.AddColumn("[bold]Foreign Keys[/]");

        foreach (var t in diff.Tables.Where(x => x.HasChanges))
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

        // 3. Detailed Tree View
        if (detailed)
        {
            RenderDetailedTree(diff, console);
        }
    }

    public static void RenderDetailedTree(SchemaDiff diff, IAnsiConsole console)
    {
        var root = new Tree("[bold underline]Detailed Changes[/]");

        foreach (var t in diff.Tables.Where(x => x.HasChanges))
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

        console.Write(root);
    }

    public static string RenderMarkdown(SchemaDiff diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Schema Diff Summary");
        sb.AppendLine();
        sb.AppendLine($"- **HasChanges**: `{diff.HasChanges}`");
        sb.AppendLine($"- **Added Tables**: {diff.AddedTables.Count()}");
        sb.AppendLine($"- **Modified Tables**: {diff.ModifiedTables.Count()}");
        sb.AppendLine($"- **Deleted Tables**: {diff.DeletedTables.Count()}");
        sb.AppendLine();

        sb.AppendLine("| Status | Table | Schema | Added Cols | Mod Cols | Del Cols |");
        sb.AppendLine("|---|---|---|---|---|---|");

        foreach (var t in diff.Tables.Where(x => x.HasChanges))
        {
            sb.AppendLine($"| {t.Kind} | `{t.TableName}` | `{t.Schema}` | {t.AddedColumns.Count()} | {t.ModifiedColumns.Count()} | {t.DeletedColumns.Count()} |");
        }

        return sb.ToString();
    }

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
