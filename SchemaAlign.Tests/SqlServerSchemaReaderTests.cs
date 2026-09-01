using SchemaAlign.Models;
using SchemaAlign.Readers.SqlServer;
using Xunit;

namespace SchemaAlign.Tests;

public class SqlServerSchemaReaderTests
{
    private class FakeSqlCatalogExecutor : ISqlCatalogExecutor
    {
        public List<TableCatalogRow> Tables { get; set; } = new();
        public List<ColumnCatalogRow> Columns { get; set; } = new();
        public List<KeyConstraintCatalogRow> KeyConstraints { get; set; } = new();
        public List<ForeignKeyCatalogRow> ForeignKeys { get; set; } = new();
        public List<ExtendedPropertyCatalogRow> ExtendedProperties { get; set; } = new();

        public IReadOnlyList<TableCatalogRow> GetTables(string connectionString) => Tables;
        public IReadOnlyList<ColumnCatalogRow> GetColumns(string connectionString) => Columns;
        public IReadOnlyList<KeyConstraintCatalogRow> GetKeyConstraints(string connectionString) => KeyConstraints;
        public IReadOnlyList<ForeignKeyCatalogRow> GetForeignKeys(string connectionString) => ForeignKeys;
        public IReadOnlyList<ExtendedPropertyCatalogRow> GetExtendedProperties(string connectionString) => ExtendedProperties;
    }

    [Fact]
    public void Read_WithMockCatalog_PopulatesTablesAndColumns()
    {
        // Arrange
        var fake = new FakeSqlCatalogExecutor();
        fake.Tables.Add(new TableCatalogRow(1001, "dbo", "TblAlpha"));
        fake.Tables.Add(new TableCatalogRow(1002, "sales", "TblBeta"));

        fake.Columns.Add(new ColumnCatalogRow(1001, 1, "Id", "int", 4, 10, 0, false, true, null));
        fake.Columns.Add(new ColumnCatalogRow(1001, 2, "Title", "nvarchar", 100, 0, 0, false, false, null)); // max_length 100 bytes = 50 chars for nvarchar
        fake.Columns.Add(new ColumnCatalogRow(1001, 3, "Price", "decimal", 9, 18, 2, true, false, "((0.00))"));

        fake.Columns.Add(new ColumnCatalogRow(1002, 1, "BetaId", "bigint", 8, 19, 0, false, false, null));
        fake.Columns.Add(new ColumnCatalogRow(1002, 2, "AlphaId", "int", 4, 10, 0, false, false, null));
        fake.Columns.Add(new ColumnCatalogRow(1002, 3, "RawData", "varbinary", -1, 0, 0, true, false, null));

        fake.KeyConstraints.Add(new KeyConstraintCatalogRow(1001, "Id", "PK"));
        fake.KeyConstraints.Add(new KeyConstraintCatalogRow(1002, "BetaId", "PK"));

        fake.ForeignKeys.Add(new ForeignKeyCatalogRow(
            "FK_Beta_Alpha",
            "sales", "TblBeta", "AlphaId",
            "dbo", "TblAlpha", "Id"
        ));

        fake.ExtendedProperties.Add(new ExtendedPropertyCatalogRow(1001, 0, 1, "MS_Description", "Alpha entity table"));
        fake.ExtendedProperties.Add(new ExtendedPropertyCatalogRow(1001, 2, 1, "MS_Description", "The title description"));

        var reader = new SqlServerSchemaReader(fake);

        // Act
        var schema = reader.Read("Server=fake;Database=fake;");

        // Assert
        schema.Tables.Should().HaveCount(2);

        var alpha = schema.FindTable("TblAlpha");
        alpha.Should().NotBeNull();
        alpha!.Schema.Should().Be("dbo");
        alpha.Comment.Should().Be("Alpha entity table");
        alpha.Columns.Should().HaveCount(3);

        var idCol = alpha.FindColumn("Id");
        idCol.Should().NotBeNull();
        idCol!.Type.Should().Be(StandardType.Int);
        idCol.IsPrimaryKey.Should().BeTrue();
        idCol.IsIdentity.Should().BeTrue();
        idCol.IsNullable.Should().BeFalse();

        var titleCol = alpha.FindColumn("Title");
        titleCol.Should().NotBeNull();
        titleCol!.Type.Should().Be(StandardType.String);
        titleCol.Length.Should().Be(50);
        titleCol.Comment.Should().Be("The title description");

        var priceCol = alpha.FindColumn("Price");
        priceCol.Should().NotBeNull();
        priceCol!.Type.Should().Be(StandardType.Decimal);
        priceCol.Precision.Should().Be(18);
        priceCol.Scale.Should().Be(2);
        priceCol.DefaultValue.Should().Be("((0.00))");

        var beta = schema.FindTable("TblBeta");
        beta.Should().NotBeNull();
        beta!.Schema.Should().Be("sales");
        beta.FindColumn("BetaId")!.IsPrimaryKey.Should().BeTrue();
        beta.FindColumn("RawData")!.Type.Should().Be(StandardType.ByteArray);
        beta.FindColumn("RawData")!.Length.Should().Be(-1);

        beta.ForeignKeys.Should().HaveCount(1);
        var fk = beta.ForeignKeys[0];
        fk.ConstraintName.Should().Be("FK_Beta_Alpha");
        fk.DependentTable.Should().Be("TblBeta");
        fk.DependentColumn.Should().Be("AlphaId");
        fk.PrincipalTable.Should().Be("TblAlpha");
        fk.PrincipalColumn.Should().Be("Id");
    }

    [Fact]
    public void Read_CompositePrimaryKey_CorrectlyFlagsAllKeyColumns()
    {
        // Arrange
        var fake = new FakeSqlCatalogExecutor();
        fake.Tables.Add(new TableCatalogRow(2001, "dbo", "TblCompositeKey"));

        fake.Columns.Add(new ColumnCatalogRow(2001, 1, "TenantId", "int", 4, 10, 0, false, false, null));
        fake.Columns.Add(new ColumnCatalogRow(2001, 2, "RecordId", "int", 4, 10, 0, false, false, null));
        fake.Columns.Add(new ColumnCatalogRow(2001, 3, "Info", "varchar", 200, 0, 0, true, false, null));

        fake.KeyConstraints.Add(new KeyConstraintCatalogRow(2001, "TenantId", "PK"));
        fake.KeyConstraints.Add(new KeyConstraintCatalogRow(2001, "RecordId", "PK"));

        var reader = new SqlServerSchemaReader(fake);

        // Act
        var schema = reader.Read("Server=fake;Database=fake;");

        // Assert
        var table = schema.FindTable("TblCompositeKey");
        table.Should().NotBeNull();
        table!.PrimaryKeys.Should().BeEquivalentTo(new[] { "TenantId", "RecordId" });
        table.FindColumn("TenantId")!.IsPrimaryKey.Should().BeTrue();
        table.FindColumn("RecordId")!.IsPrimaryKey.Should().BeTrue();
        table.FindColumn("Info")!.IsPrimaryKey.Should().BeFalse();
    }

    [Fact]
    public void Read_EmptyCatalog_ReturnsEmptySchema()
    {
        // Arrange
        var fake = new FakeSqlCatalogExecutor();
        var reader = new SqlServerSchemaReader(fake);

        // Act
        var schema = reader.Read("Server=fake;Database=fake;");

        // Assert
        schema.Tables.Should().BeEmpty();
    }

    [Fact]
    public void Read_EmptyOrWhitespaceConnectionString_ReturnsEmptySchema()
    {
        var fake = new FakeSqlCatalogExecutor();
        var reader = new SqlServerSchemaReader(fake);

        var schema = reader.Read("   ");
        schema.Tables.Should().BeEmpty();
    }
}
