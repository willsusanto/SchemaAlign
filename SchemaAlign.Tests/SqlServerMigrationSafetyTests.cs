using SchemaAlign.Appliers.SqlServer;
using SchemaAlign.Diff;
using SchemaAlign.Models;

namespace SchemaAlign.Tests;

public class SqlServerMigrationSafetyTests
{
    [Fact]
    public void Generate_DropColumn_DropsDependentForeignKeysAndDefaultConstraintsFirst()
    {
        var sourceTable = new TableSchema { Name = "tbl_masked_sample", Schema = "dbo" };
        sourceTable.AddColumn(new ColumnSchema { Name = "ColId", Type = StandardType.Int, IsPrimaryKey = true });
        sourceTable.AddColumn(new ColumnSchema { Name = "ColObsolete", Type = StandardType.Int });

        var targetTable = new TableSchema { Name = "tbl_masked_sample", Schema = "dbo" };
        targetTable.AddColumn(new ColumnSchema { Name = "ColId", Type = StandardType.Int, IsPrimaryKey = true });

        var diff = new SchemaDiff();
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_sample",
            Schema = "dbo",
            Kind = DiffKind.Modified,
            Source = sourceTable,
            Target = targetTable
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "ColObsolete",
            Kind = DiffKind.Deleted,
            Source = sourceTable.Columns["ColObsolete"]
        });
        diff.Tables.Add(tableDiff);

        var sql = SqlServerMigrationGenerator.Generate(diff, new SqlServerApplierOptions { AllowDrops = true });

        // Must clean up constraints on the column before the ALTER TABLE DROP COLUMN statement
        sql.Should().Contain("sys.foreign_key_columns");
        sql.Should().Contain("sys.default_constraints");
        sql.Should().Contain("ALTER TABLE [dbo].[tbl_masked_sample] DROP COLUMN [ColObsolete]");
    }

    [Fact]
    public void Generate_AddForeignKey_IncludesGuardsForBothColumnsExisting()
    {
        var diff = new SchemaDiff();
        var parentTable = new TableSchema { Name = "tbl_masked_parent", Schema = "dbo" };
        parentTable.AddColumn(new ColumnSchema { Name = "ColIdParent", Type = StandardType.Int, IsPrimaryKey = true });

        var childTable = new TableSchema { Name = "tbl_masked_child", Schema = "dbo" };
        childTable.AddColumn(new ColumnSchema { Name = "ColIdChild", Type = StandardType.Int, IsPrimaryKey = true });
        childTable.AddColumn(new ColumnSchema { Name = "ColIdParent", Type = StandardType.Int });

        var fk = new ForeignKeySchema
        {
            ConstraintName = "FK_tbl_child_parent",
            PrincipalTable = "tbl_masked_parent",
            PrincipalColumn = "ColIdParent",
            DependentTable = "tbl_masked_child",
            DependentColumn = "ColIdParent"
        };
        childTable.AddForeignKey(fk);

        var childDiff = new TableDiff
        {
            TableName = "tbl_masked_child",
            Schema = "dbo",
            Kind = DiffKind.Modified,
            Target = childTable
        };
        childDiff.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = fk.ConstraintName,
            Kind = DiffKind.Added,
            Target = fk
        });
        diff.Tables.Add(childDiff);

        var sql = SqlServerMigrationGenerator.Generate(diff, new SqlServerApplierOptions());

        // Guard must check foreign key does not exist AND both columns exist
        sql.Should().Contain("sys.foreign_keys");
        sql.Should().Contain("sys.columns");
        sql.Should().Contain("ColIdParent");
    }

    [Fact]
    public void Generate_AddForeignKey_ResolvesActualPrincipalPrimaryKeyIfGenericIdSupplied()
    {
        var diff = new SchemaDiff();
        var parentTable = new TableSchema { Name = "tbl_masked_parent", Schema = "dbo" };
        parentTable.AddColumn(new ColumnSchema { Name = "IdParentCustom", Type = StandardType.Int, IsPrimaryKey = true });

        var childTable = new TableSchema { Name = "tbl_masked_child", Schema = "dbo" };
        childTable.AddColumn(new ColumnSchema { Name = "IdChildCustom", Type = StandardType.Int, IsPrimaryKey = true });
        childTable.AddColumn(new ColumnSchema { Name = "IdParentCustom", Type = StandardType.Int });

        // Suppose diagram had PrincipalColumn = "Id" because parent PK wasn't parsed in diagram
        var fk = new ForeignKeySchema
        {
            ConstraintName = "FK_tbl_child_parent",
            PrincipalTable = "tbl_masked_parent",
            PrincipalColumn = "Id",
            DependentTable = "tbl_masked_child",
            DependentColumn = "IdParentCustom"
        };
        childTable.AddForeignKey(fk);

        diff.SourceSchema = new DatabaseSchema();
        diff.SourceSchema.AddTable(parentTable);

        var childDiff = new TableDiff
        {
            TableName = "tbl_masked_child",
            Schema = "dbo",
            Kind = DiffKind.Modified,
            Target = childTable
        };
        childDiff.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = fk.ConstraintName,
            Kind = DiffKind.Added,
            Target = fk
        });
        diff.Tables.Add(childDiff);

        var sql = SqlServerMigrationGenerator.Generate(diff, new SqlServerApplierOptions());

        // Should resolve [IdParentCustom] instead of [Id]
        sql.Should().Contain("REFERENCES [dbo].[tbl_masked_parent] ([IdParentCustom])");
    }
}
