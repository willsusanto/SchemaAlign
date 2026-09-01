using SchemaAlign.Cli.Rendering;
using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Tests.Cli;

public class DiffFilterTests
{
    [Fact]
    public void HasDestructiveChanges_WhenTableOrColumnDeleted_ReturnsTrue()
    {
        var diff = new SchemaDiff();

        var deletedTable = new TableDiff { TableName = "OldTable", Kind = DiffKind.Deleted };
        diff.Tables.Add(deletedTable);

        DiffFilter.HasDestructiveChanges(diff).Should().BeTrue();
    }

    [Fact]
    public void HasDestructiveChanges_WhenOnlyAdditionsAndModifications_ReturnsFalse()
    {
        var diff = new SchemaDiff();

        var addedTable = new TableDiff { TableName = "NewTable", Kind = DiffKind.Added };
        var modTable = new TableDiff { TableName = "ExistingTable", Kind = DiffKind.Modified };
        modTable.Columns.Add(new ColumnDiff { ColumnName = "ColA", Kind = DiffKind.Added });
        modTable.Columns.Add(new ColumnDiff { ColumnName = "ColB", Kind = DiffKind.Modified });

        diff.Tables.Add(addedTable);
        diff.Tables.Add(modTable);

        DiffFilter.HasDestructiveChanges(diff).Should().BeFalse();
    }

    [Fact]
    public void Filter_WithoutAllowDrops_RemovesAllDeletions()
    {
        var diff = new SchemaDiff();

        var deletedTable = new TableDiff { TableName = "OldTable", Kind = DiffKind.Deleted };
        var modTable = new TableDiff { TableName = "ExistingTable", Kind = DiffKind.Modified };
        modTable.Columns.Add(new ColumnDiff { ColumnName = "ColA", Kind = DiffKind.Added });
        modTable.Columns.Add(new ColumnDiff { ColumnName = "ColOld", Kind = DiffKind.Deleted });
        modTable.ForeignKeys.Add(new ForeignKeyDiff { ConstraintName = "FK_Old", Kind = DiffKind.Deleted });

        diff.Tables.Add(deletedTable);
        diff.Tables.Add(modTable);

        var filtered = DiffFilter.Filter(diff, allowDrops: false);

        filtered.DeletedTables.Should().BeEmpty();
        var existing = filtered.FindTable("ExistingTable");
        existing.Should().NotBeNull();
        existing!.DeletedColumns.Should().BeEmpty();
        existing.DeletedForeignKeys.Should().BeEmpty();
        existing.AddedColumns.Should().HaveCount(1);
    }
}
