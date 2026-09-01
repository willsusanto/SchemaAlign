using FluentAssertions;
using SchemaAlign.Models;
using Xunit;

namespace SchemaAlign.Tests.Models;

public class DatabaseSchemaTests
{
    [Fact]
    public void DatabaseSchema_ShouldStoreAndFindTablesCaseInsensitively()
    {
        var schema = new DatabaseSchema();
        var table = new TableSchema { Name = "Users", Schema = "dbo" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table.AddColumn(new ColumnSchema { Name = "Email", Type = StandardType.String, Length = 100, IsNullable = false });

        schema.AddTable(table);

        schema.Tables.Should().ContainKey("Users");
        schema.FindTable("users").Should().NotBeNull();
        schema.FindTable("USERS")!.Columns.Should().ContainKey("email");
        schema.FindTable("Users")!.PrimaryKeys.Should().Contain("Id");
    }

    [Fact]
    public void DatabaseSchema_GetOrCreateTable_ShouldReturnExistingOrNewTable()
    {
        var schema = new DatabaseSchema();
        var table1 = schema.GetOrCreateTable("Orders", "sales");
        table1.Schema.Should().Be("sales");

        var table2 = schema.GetOrCreateTable("orders");
        table2.Should().BeSameAs(table1);
    }

    [Fact]
    public void TableSchema_FindColumn_ShouldBeCaseInsensitive()
    {
        var table = new TableSchema { Name = "Products" };
        table.AddColumn(new ColumnSchema { Name = "Price", Type = StandardType.Decimal, Precision = 18, Scale = 2 });

        table.FindColumn("price").Should().NotBeNull();
        table.FindColumn("PRICE")!.Precision.Should().Be(18);
        table.FindColumn("NonExistent").Should().BeNull();
    }

    [Fact]
    public void TableSchema_CompositePrimaryKeys_ShouldBeTracked()
    {
        var table = new TableSchema { Name = "OrderItems" };
        table.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.Int, IsPrimaryKey = true });
        table.AddColumn(new ColumnSchema { Name = "ItemId", Type = StandardType.Int, IsPrimaryKey = true });
        table.AddColumn(new ColumnSchema { Name = "Quantity", Type = StandardType.Int });

        table.PrimaryKeys.Should().BeEquivalentTo(new[] { "OrderId", "ItemId" });
    }

    [Fact]
    public void TableSchema_AddForeignKey_ShouldTrackRelation()
    {
        var schema = new DatabaseSchema();
        var users = new TableSchema { Name = "Users" };
        users.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        schema.AddTable(users);

        var orders = new TableSchema { Name = "Orders" };
        orders.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        orders.AddColumn(new ColumnSchema { Name = "UserId", Type = StandardType.Int, IsNullable = false });
        
        var fk = new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_Users_UserId",
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "UserId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        };
        orders.AddForeignKey(fk);
        schema.AddTable(orders);

        orders.ForeignKeys.Should().HaveCount(1);
        orders.ForeignKeys[0].PrincipalTable.Should().Be("Users");
    }
}

