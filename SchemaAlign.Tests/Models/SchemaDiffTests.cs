using FluentAssertions;
using SchemaAlign.Models;
using SchemaAlign.Models.Diff;
using Xunit;

namespace SchemaAlign.Tests.Models;

public class SchemaDiffTests
{
    [Fact]
    public void Compare_WhenSchemasAreIdentical_ReturnsNoDiffs()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var table1 = new TableSchema { Name = "Users" };
        table1.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table1.AddColumn(new ColumnSchema { Name = "Username", Type = StandardType.String, Length = 50, IsNullable = false });

        var table2 = new TableSchema { Name = "Users" };
        table2.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table2.AddColumn(new ColumnSchema { Name = "Username", Type = StandardType.String, Length = 50, IsNullable = false });

        source.AddTable(table1);
        target.AddTable(table2);

        var diff = SchemaDiff.Compare(source, target);

        diff.HasChanges.Should().BeFalse();
        diff.TableDiffs.Should().BeEmpty();
    }

    [Fact]
    public void Compare_WhenTableAddedInSource_MarksTableAsAdded()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var table = new TableSchema { Name = "Orders" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.BigInt, IsPrimaryKey = true });
        source.AddTable(table);

        var diff = SchemaDiff.Compare(source, target);

        diff.HasChanges.Should().BeTrue();
        diff.TableDiffs.Should().ContainKey("Orders");
        var tableDiff = diff.TableDiffs["Orders"];
        tableDiff.DiffType.Should().Be(DiffType.Added);
        tableDiff.SourceTable.Should().NotBeNull();
        tableDiff.TargetTable.Should().BeNull();
    }

    [Fact]
    public void Compare_WhenTableDeletedFromSource_MarksTableAsDeleted()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var table = new TableSchema { Name = "LegacyLogs" };
        target.AddTable(table);

        var diff = SchemaDiff.Compare(source, target);

        diff.HasChanges.Should().BeTrue();
        diff.TableDiffs.Should().ContainKey("LegacyLogs");
        var tableDiff = diff.TableDiffs["LegacyLogs"];
        tableDiff.DiffType.Should().Be(DiffType.Deleted);
        tableDiff.SourceTable.Should().BeNull();
        tableDiff.TargetTable.Should().NotBeNull();
    }

    [Fact]
    public void Compare_WhenColumnsAddedModifiedAndDeleted_MarksColumnDiffsCorrectly()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var srcTable = new TableSchema { Name = "Products" };
        srcTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.BigInt, IsPrimaryKey = true }); // Modified type Int -> BigInt
        srcTable.AddColumn(new ColumnSchema { Name = "Price", Type = StandardType.Decimal, Precision = 18, Scale = 2, IsNullable = false }); // Unchanged
        srcTable.AddColumn(new ColumnSchema { Name = "Description", Type = StandardType.String, Length = 500, IsNullable = true }); // Added column

        var tgtTable = new TableSchema { Name = "Products" };
        tgtTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        tgtTable.AddColumn(new ColumnSchema { Name = "Price", Type = StandardType.Decimal, Precision = 18, Scale = 2, IsNullable = false });
        tgtTable.AddColumn(new ColumnSchema { Name = "OldField", Type = StandardType.String }); // Deleted column

        source.AddTable(srcTable);
        target.AddTable(tgtTable);

        var diff = SchemaDiff.Compare(source, target);

        diff.HasChanges.Should().BeTrue();
        var tableDiff = diff.TableDiffs["Products"];
        tableDiff.DiffType.Should().Be(DiffType.Modified);

        tableDiff.ColumnDiffs.Should().ContainKey("Id");
        tableDiff.ColumnDiffs["Id"].DiffType.Should().Be(DiffType.Modified);

        tableDiff.ColumnDiffs.Should().ContainKey("Description");
        tableDiff.ColumnDiffs["Description"].DiffType.Should().Be(DiffType.Added);

        tableDiff.ColumnDiffs.Should().ContainKey("OldField");
        tableDiff.ColumnDiffs["OldField"].DiffType.Should().Be(DiffType.Deleted);

        tableDiff.ColumnDiffs.Should().NotContainKey("Price");
    }
}
