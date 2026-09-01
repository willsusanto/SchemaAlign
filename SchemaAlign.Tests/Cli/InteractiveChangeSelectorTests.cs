using SchemaAlign.Cli.Rendering;
using SchemaAlign.Diff;

namespace SchemaAlign.Tests.Cli;

public class InteractiveChangeSelectorTests
{
    [Fact]
    public void FlattenChanges_IdentifiesDestructiveAndNonDestructiveChanges()
    {
        var diff = new SchemaDiff();

        var addedTable = new TableDiff { TableName = "NewTable", Kind = DiffKind.Added };
        var deletedTable = new TableDiff { TableName = "OldTable", Kind = DiffKind.Deleted };
        var modTable = new TableDiff { TableName = "ExistingTable", Kind = DiffKind.Modified };
        modTable.Columns.Add(new ColumnDiff { ColumnName = "ColA", Kind = DiffKind.Added });
        modTable.Columns.Add(new ColumnDiff { ColumnName = "ColOld", Kind = DiffKind.Deleted });

        diff.Tables.Add(addedTable);
        diff.Tables.Add(deletedTable);
        diff.Tables.Add(modTable);

        var items = InteractiveChangeSelector.FlattenChanges(diff);

        items.Should().HaveCount(4);
        items.Should().ContainSingle(x => x.Kind == ChangeItemKind.AddTable && !x.IsDestructive);
        items.Should().ContainSingle(x => x.Kind == ChangeItemKind.DropTable && x.IsDestructive);
        items.Should().ContainSingle(x => x.Kind == ChangeItemKind.AddColumn && !x.IsDestructive);
        items.Should().ContainSingle(x => x.Kind == ChangeItemKind.DropColumn && x.IsDestructive);
    }

    [Fact]
    public void ApplySelection_IncludesOnlySelectedItems()
    {
        var diff = new SchemaDiff();

        var addedTable = new TableDiff { TableName = "Table1", Kind = DiffKind.Added };
        var modTable = new TableDiff { TableName = "Table2", Kind = DiffKind.Modified };
        modTable.Columns.Add(new ColumnDiff { ColumnName = "Col1", Kind = DiffKind.Added });
        modTable.Columns.Add(new ColumnDiff { ColumnName = "Col2", Kind = DiffKind.Deleted });

        diff.Tables.Add(addedTable);
        diff.Tables.Add(modTable);

        var allItems = InteractiveChangeSelector.FlattenChanges(diff);
        var selectedOnlyAdditions = allItems.Where(x => !x.IsDestructive).ToList();

        var filtered = InteractiveChangeSelector.ApplySelection(diff, selectedOnlyAdditions);

        filtered.AddedTables.Should().ContainSingle(t => t.TableName == "Table1");
        var mod = filtered.FindTable("Table2");
        mod.Should().NotBeNull();
        mod!.AddedColumns.Should().ContainSingle(c => c.ColumnName == "Col1");
        mod.DeletedColumns.Should().BeEmpty();
    }
}
