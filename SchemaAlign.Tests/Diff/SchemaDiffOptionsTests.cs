using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Tests.Diff;

public class SchemaDiffOptionsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Calculate_Modes_AlwaysReportOmittedColumnsButOnlySnapshotRemovesOmittedForeignKeys(bool snapshot)
    {
        var source = new DatabaseSchema();
        var current = new TableSchema { Name = "MaskedRecord" };
        current.AddColumn(new ColumnSchema { Name = "MaskedKey", Type = StandardType.Int });
        current.AddColumn(new ColumnSchema { Name = "MaskedPriorColumn", Type = StandardType.String });
        current.ForeignKeys.Add(new ForeignKeySchema
        {
            ConstraintName = "MaskedRelationship",
            DependentTable = "MaskedRecord",
            DependentColumn = "MaskedKey",
            PrincipalTable = "MaskedParent",
            PrincipalColumn = "MaskedKey"
        });
        source.AddTable(current);
        var target = new DatabaseSchema();
        var desired = new TableSchema { Name = "MaskedRecord" };
        desired.AddColumn(new ColumnSchema { Name = "MaskedKey", Type = StandardType.Int });
        target.AddTable(desired);

        var result = SchemaDiffCalculator.Calculate(source, target,
            snapshot ? SchemaDiffOptions.FullSnapshot : SchemaDiffOptions.Incremental);

        var changes = result.FindTable("MaskedRecord")!;
        changes.DeletedColumns.Should().ContainSingle(column => column.ColumnName == "MaskedPriorColumn");
        changes.ForeignKeys.Count(key => key.Kind == DiffKind.Deleted).Should().Be(snapshot ? 1 : 0);
    }
    [Fact]
    public void Calculate_IncrementalMode_OmitsDeletedTablesAndForeignKeys()
    {
        var source = new DatabaseSchema();
        var table1 = new TableSchema { Name = "TableA" };
        table1.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        source.AddTable(table1);

        var target = new DatabaseSchema(); // Target is missing TableA (omitted in sprint diagram)

        // Incremental mode: table present in source but missing in target is ignored
        var diff = SchemaDiffCalculator.Calculate(source, target, SchemaDiffOptions.Incremental);

        diff.HasChanges.Should().BeFalse();
        diff.DeletedTables.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_FullSnapshotMode_MarksMissingTablesAsDeleted()
    {
        var source = new DatabaseSchema();
        var table1 = new TableSchema { Name = "TableA" };
        table1.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        source.AddTable(table1);

        var target = new DatabaseSchema();

        // Full snapshot mode: table present in source but missing in target is deleted
        var diff = SchemaDiffCalculator.Calculate(source, target, SchemaDiffOptions.FullSnapshot);

        diff.HasChanges.Should().BeTrue();
        diff.DeletedTables.Should().ContainSingle(t => t.TableName == "TableA");
    }
}
