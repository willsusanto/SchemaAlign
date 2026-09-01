using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Tests.Diff;

public class SchemaDiffOptionsTests
{
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
