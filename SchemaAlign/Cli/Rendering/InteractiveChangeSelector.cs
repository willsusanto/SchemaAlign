using SchemaAlign.Diff;
using Spectre.Console;

namespace SchemaAlign.Cli.Rendering;

public enum ChangeItemKind
{
    AddTable,
    DropTable,
    AddColumn,
    ModifyColumn,
    DropColumn,
    AddForeignKey,
    ModifyForeignKey,
    DropForeignKey
}

public class ChangeItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public ChangeItemKind Kind { get; set; }
    public bool IsDestructive => Kind == ChangeItemKind.DropTable || Kind == ChangeItemKind.DropColumn || Kind == ChangeItemKind.DropForeignKey;
    public ColumnDiff? ColumnDiff { get; set; }
    public ForeignKeyDiff? ForeignKeyDiff { get; set; }
    public TableDiff? TableDiff { get; set; }

    public override string ToString() => DisplayText;
}

public static class InteractiveChangeSelector
{
    public static List<ChangeItem> FlattenChanges(SchemaDiff diff)
    {
        var items = new List<ChangeItem>();

        foreach (var t in diff.Tables.Where(x => x.HasChanges))
        {
            if (t.Kind == DiffKind.Added)
            {
                items.Add(new ChangeItem
                {
                    Id = $"table_add_{t.TableName}",
                    TableName = t.TableName,
                    Kind = ChangeItemKind.AddTable,
                    DisplayText = $"[green]+ ADD TABLE {Markup.Escape(t.TableName)}[/]",
                    TableDiff = t
                });
            }
            else if (t.Kind == DiffKind.Deleted)
            {
                items.Add(new ChangeItem
                {
                    Id = $"table_drop_{t.TableName}",
                    TableName = t.TableName,
                    Kind = ChangeItemKind.DropTable,
                    DisplayText = $"[red]⚠️  DROP TABLE {Markup.Escape(t.TableName)} (DESTRUCTIVE)[/]",
                    TableDiff = t
                });
            }
            else if (t.Kind == DiffKind.Modified)
            {
                foreach (var c in t.Columns.Where(x => x.Kind != DiffKind.Unchanged))
                {
                    if (c.Kind == DiffKind.Added)
                    {
                        var addType = c.Target != null ? $"{c.Target.Type}{(c.Target.Length.HasValue ? $"({c.Target.Length})" : "")}" : "";
                        items.Add(new ChangeItem
                        {
                            Id = $"col_add_{t.TableName}_{c.ColumnName}",
                            TableName = t.TableName,
                            Kind = ChangeItemKind.AddColumn,
                            DisplayText = $"[green]+ ADD COLUMN {Markup.Escape(t.TableName)}.{Markup.Escape(c.ColumnName)} ({addType})[/]",
                            TableDiff = t,
                            ColumnDiff = c
                        });
                    }
                    else if (c.Kind == DiffKind.Deleted)
                    {
                        items.Add(new ChangeItem
                        {
                            Id = $"col_drop_{t.TableName}_{c.ColumnName}",
                            TableName = t.TableName,
                            Kind = ChangeItemKind.DropColumn,
                            DisplayText = $"[red]⚠️  DROP COLUMN {Markup.Escape(t.TableName)}.{Markup.Escape(c.ColumnName)} (DESTRUCTIVE)[/]",
                            TableDiff = t,
                            ColumnDiff = c
                        });
                    }
                    else if (c.Kind == DiffKind.Modified)
                    {
                        items.Add(new ChangeItem
                        {
                            Id = $"col_mod_{t.TableName}_{c.ColumnName}",
                            TableName = t.TableName,
                            Kind = ChangeItemKind.ModifyColumn,
                            DisplayText = $"[yellow]~ MODIFY COLUMN {Markup.Escape(t.TableName)}.{Markup.Escape(c.ColumnName)} ({c.Changes})[/]",
                            TableDiff = t,
                            ColumnDiff = c
                        });
                    }
                }

                foreach (var f in t.ForeignKeys.Where(x => x.Kind != DiffKind.Unchanged))
                {
                    var name = f.ConstraintName ?? "(unnamed)";
                    if (f.Kind == DiffKind.Added)
                    {
                        items.Add(new ChangeItem
                        {
                            Id = $"fk_add_{t.TableName}_{name}",
                            TableName = t.TableName,
                            Kind = ChangeItemKind.AddForeignKey,
                            DisplayText = $"[green]+ ADD FK {Markup.Escape(t.TableName)}.{Markup.Escape(name)}[/]",
                            TableDiff = t,
                            ForeignKeyDiff = f
                        });
                    }
                    else if (f.Kind == DiffKind.Deleted)
                    {
                        items.Add(new ChangeItem
                        {
                            Id = $"fk_drop_{t.TableName}_{name}",
                            TableName = t.TableName,
                            Kind = ChangeItemKind.DropForeignKey,
                            DisplayText = $"[red]⚠️  DROP FK {Markup.Escape(t.TableName)}.{Markup.Escape(name)} (DESTRUCTIVE)[/]",
                            TableDiff = t,
                            ForeignKeyDiff = f
                        });
                    }
                    else if (f.Kind == DiffKind.Modified)
                    {
                        items.Add(new ChangeItem
                        {
                            Id = $"fk_mod_{t.TableName}_{name}",
                            TableName = t.TableName,
                            Kind = ChangeItemKind.ModifyForeignKey,
                            DisplayText = $"[yellow]~ MODIFY FK {Markup.Escape(t.TableName)}.{Markup.Escape(name)}[/]",
                            TableDiff = t,
                            ForeignKeyDiff = f
                        });
                    }
                }
            }
        }

        return items;
    }

    public static SchemaDiff PromptSelection(SchemaDiff diff, IAnsiConsole console, bool preselectDrops = false)
    {
        var items = FlattenChanges(diff);
        if (items.Count == 0)
            return diff;

        var prompt = new MultiSelectionPrompt<ChangeItem>()
            .Title("[bold blue]Select changes to apply to target:[/]")
            .PageSize(15)
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm)[/]")
            .UseConverter(item => item.DisplayText);

        foreach (var item in items)
        {
            var choice = prompt.AddChoice(item);
            if (!item.IsDestructive || preselectDrops)
            {
                choice.Select();
            }
        }

        var selectedItems = console.Prompt(prompt);
        return ApplySelection(diff, selectedItems);
    }

    public static SchemaDiff ApplySelection(SchemaDiff diff, IEnumerable<ChangeItem> selectedItems)
    {
        var selectedSet = new HashSet<string>(selectedItems.Select(x => x.Id));

        var filtered = new SchemaDiff
        {
            SourceSchema = diff.SourceSchema,
            TargetSchema = diff.TargetSchema
        };

        foreach (var t in diff.Tables)
        {
            if (t.Kind == DiffKind.Added)
            {
                if (selectedSet.Contains($"table_add_{t.TableName}"))
                {
                    filtered.Tables.Add(t);
                }
                continue;
            }

            if (t.Kind == DiffKind.Deleted)
            {
                if (selectedSet.Contains($"table_drop_{t.TableName}"))
                {
                    filtered.Tables.Add(t);
                }
                continue;
            }

            if (t.Kind == DiffKind.Modified)
            {
                var newTable = new TableDiff
                {
                    TableName = t.TableName,
                    Schema = t.Schema,
                    Kind = t.Kind,
                    Source = t.Source,
                    Target = t.Target
                };

                foreach (var c in t.Columns)
                {
                    if (c.Kind == DiffKind.Unchanged)
                    {
                        newTable.Columns.Add(c);
                        continue;
                    }

                    var id = c.Kind switch
                    {
                        DiffKind.Added => $"col_add_{t.TableName}_{c.ColumnName}",
                        DiffKind.Deleted => $"col_drop_{t.TableName}_{c.ColumnName}",
                        DiffKind.Modified => $"col_mod_{t.TableName}_{c.ColumnName}",
                        _ => string.Empty
                    };

                    if (selectedSet.Contains(id))
                    {
                        newTable.Columns.Add(c);
                    }
                }

                foreach (var f in t.ForeignKeys)
                {
                    if (f.Kind == DiffKind.Unchanged)
                    {
                        newTable.ForeignKeys.Add(f);
                        continue;
                    }

                    var name = f.ConstraintName ?? "(unnamed)";
                    var id = f.Kind switch
                    {
                        DiffKind.Added => $"fk_add_{t.TableName}_{name}",
                        DiffKind.Deleted => $"fk_drop_{t.TableName}_{name}",
                        DiffKind.Modified => $"fk_mod_{t.TableName}_{name}",
                        _ => string.Empty
                    };

                    if (selectedSet.Contains(id))
                    {
                        newTable.ForeignKeys.Add(f);
                    }
                }

                var hasColChanges = newTable.Columns.Any(c => c.Kind != DiffKind.Unchanged);
                var hasFkChanges = newTable.ForeignKeys.Any(f => f.Kind != DiffKind.Unchanged);
                newTable.Kind = (hasColChanges || hasFkChanges) ? DiffKind.Modified : DiffKind.Unchanged;

                filtered.Tables.Add(newTable);
            }
        }

        return filtered;
    }
}
