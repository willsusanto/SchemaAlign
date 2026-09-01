using AwesomeAssertions;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Xunit;

namespace SchemaAlign.Tests.Diff;

public class SchemaDiffCalculatorTests
{
    [Fact]
    public void Calculate_EmptySchemas_ShouldHaveNoChanges()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var diff = SchemaDiffCalculator.Calculate(source, target);

        diff.HasChanges.Should().BeFalse();
        diff.Tables.Should().BeEmpty();
        diff.AddedTables.Should().BeEmpty();
        diff.ModifiedTables.Should().BeEmpty();
        diff.DeletedTables.Should().BeEmpty();
        diff.UnchangedTables.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_IdenticalSchemas_ShouldReturnUnchanged()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var table1 = new TableSchema { Name = "Users", Schema = "dbo" };
        table1.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table1.AddColumn(new ColumnSchema { Name = "Email", Type = StandardType.String, Length = 100, IsNullable = false });
        source.AddTable(table1);

        var table2 = new TableSchema { Name = "Users", Schema = "dbo" };
        table2.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table2.AddColumn(new ColumnSchema { Name = "Email", Type = StandardType.String, Length = 100, IsNullable = false });
        target.AddTable(table2);

        var diff = SchemaDiffCalculator.Calculate(source, target);

        diff.HasChanges.Should().BeFalse();
        diff.Tables.Should().HaveCount(1);
        diff.UnchangedTables.Should().HaveCount(1);
        diff.AddedTables.Should().BeEmpty();
        diff.ModifiedTables.Should().BeEmpty();
        diff.DeletedTables.Should().BeEmpty();

        var tableDiff = diff.FindTable("Users");
        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Unchanged);
        tableDiff.HasChanges.Should().BeFalse();
        tableDiff.Columns.Should().HaveCount(2);
        tableDiff.Columns.Should().OnlyContain(c => c.Kind == DiffKind.Unchanged && c.Changes == ChangeDetail.None);
    }

    [Fact]
    public void Calculate_TableAdded_ShouldBeMarkedAddedWithAllColumnsAndForeignKeys()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var ordersTable = new TableSchema { Name = "Orders", Schema = "sales" };
        ordersTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        ordersTable.AddColumn(new ColumnSchema { Name = "CustomerId", Type = StandardType.Int, IsNullable = false });
        ordersTable.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_Customers",
            PrincipalTable = "Customers",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "CustomerId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });
        target.AddTable(ordersTable);

        var diff = SchemaDiffCalculator.Calculate(source, target);

        diff.HasChanges.Should().BeTrue();
        diff.AddedTables.Should().HaveCount(1);
        var tableDiff = diff.FindTable("Orders");
        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Added);
        tableDiff.Schema.Should().Be("sales");
        tableDiff.Source.Should().BeNull();
        tableDiff.Target.Should().BeSameAs(ordersTable);

        tableDiff.AddedColumns.Should().HaveCount(2);
        tableDiff.AddedForeignKeys.Should().HaveCount(1);
        tableDiff.ForeignKeys[0].Kind.Should().Be(DiffKind.Added);
        tableDiff.ForeignKeys[0].Target.Should().NotBeNull();
    }

    [Fact]
    public void Calculate_TableDeleted_ShouldBeMarkedDeletedWithAllColumnsAndForeignKeys()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var legacyTable = new TableSchema { Name = "LegacyLogs", Schema = "dbo" };
        legacyTable.AddColumn(new ColumnSchema { Name = "LogId", Type = StandardType.BigInt, IsPrimaryKey = true });
        legacyTable.AddColumn(new ColumnSchema { Name = "Message", Type = StandardType.String, Length = 500 });
        source.AddTable(legacyTable);

        var diff = SchemaDiffCalculator.Calculate(source, target);

        diff.HasChanges.Should().BeTrue();
        diff.DeletedTables.Should().HaveCount(1);
        var tableDiff = diff.FindTable("LegacyLogs");
        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Deleted);
        tableDiff.Source.Should().BeSameAs(legacyTable);
        tableDiff.Target.Should().BeNull();

        tableDiff.DeletedColumns.Should().HaveCount(2);
        tableDiff.Columns.Should().OnlyContain(c => c.Kind == DiffKind.Deleted);
    }

    [Fact]
    public void Calculate_ColumnAddedAndDeleted_ShouldMarkColumnsAndTableModified()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Users" };
        tableSource.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        tableSource.AddColumn(new ColumnSchema { Name = "OldField", Type = StandardType.String });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Users" };
        tableTarget.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        tableTarget.AddColumn(new ColumnSchema { Name = "NewField", Type = StandardType.String, Length = 50 });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);

        var tableDiff = diff.FindTable("Users");
        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Modified);

        tableDiff.AddedColumns.Should().HaveCount(1);
        tableDiff.AddedColumns.First().ColumnName.Should().Be("NewField");

        tableDiff.DeletedColumns.Should().HaveCount(1);
        tableDiff.DeletedColumns.First().ColumnName.Should().Be("OldField");

        tableDiff.ModifiedColumns.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_ColumnTypeModified_ShouldFlagTypeChanged()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Products" };
        tableSource.AddColumn(new ColumnSchema { Name = "Price", Type = StandardType.Float });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Products" };
        tableTarget.AddColumn(new ColumnSchema { Name = "Price", Type = StandardType.Decimal, Precision = 18, Scale = 2 });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var colDiff = diff.FindTable("Products")!.Columns.First(c => c.ColumnName == "Price");

        colDiff.Kind.Should().Be(DiffKind.Modified);
        colDiff.Changes.Should().HaveFlag(ChangeDetail.TypeChanged);
        colDiff.Changes.Should().HaveFlag(ChangeDetail.PrecisionChanged);
        colDiff.Changes.Should().HaveFlag(ChangeDetail.ScaleChanged);
    }

    [Fact]
    public void Calculate_ColumnLengthModified_ShouldFlagLengthChanged()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Accounts" };
        tableSource.AddColumn(new ColumnSchema { Name = "Username", Type = StandardType.String, Length = 50 });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Accounts" };
        tableTarget.AddColumn(new ColumnSchema { Name = "Username", Type = StandardType.String, Length = 100 });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var colDiff = diff.FindTable("Accounts")!.Columns.First(c => c.ColumnName == "Username");

        colDiff.Kind.Should().Be(DiffKind.Modified);
        colDiff.Changes.Should().Be(ChangeDetail.LengthChanged);
    }

    [Fact]
    public void Calculate_ColumnNullabilityModified_ShouldFlagNullabilityChanged()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Accounts" };
        tableSource.AddColumn(new ColumnSchema { Name = "Bio", Type = StandardType.String, IsNullable = true });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Accounts" };
        tableTarget.AddColumn(new ColumnSchema { Name = "Bio", Type = StandardType.String, IsNullable = false });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var colDiff = diff.FindTable("Accounts")!.Columns.First(c => c.ColumnName == "Bio");

        colDiff.Kind.Should().Be(DiffKind.Modified);
        colDiff.Changes.Should().Be(ChangeDetail.NullabilityChanged);
    }

    [Fact]
    public void Calculate_ColumnKeyStatusModified_ShouldFlagKeyStatusChanged()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Items" };
        tableSource.AddColumn(new ColumnSchema { Name = "Code", Type = StandardType.String, IsPrimaryKey = false });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Items" };
        tableTarget.AddColumn(new ColumnSchema { Name = "Code", Type = StandardType.String, IsPrimaryKey = true });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var colDiff = diff.FindTable("Items")!.Columns.First(c => c.ColumnName == "Code");

        colDiff.Kind.Should().Be(DiffKind.Modified);
        colDiff.Changes.Should().Be(ChangeDetail.KeyStatusChanged);
    }

    [Fact]
    public void Calculate_ColumnIdentityAndDefaultValueModified_ShouldFlagCorrectDetails()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Config" };
        tableSource.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsIdentity = false, DefaultValue = null });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Config" };
        tableTarget.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsIdentity = true, DefaultValue = "1" });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var colDiff = diff.FindTable("Config")!.Columns.First(c => c.ColumnName == "Id");

        colDiff.Kind.Should().Be(DiffKind.Modified);
        colDiff.Changes.Should().HaveFlag(ChangeDetail.IdentityChanged);
        colDiff.Changes.Should().HaveFlag(ChangeDetail.DefaultValueChanged);
    }

    [Fact]
    public void Calculate_ColumnMultiFieldDiff_ShouldCombineFlags()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Users" };
        tableSource.AddColumn(new ColumnSchema
        {
            Name = "Email",
            Type = StandardType.String,
            Length = 50,
            IsNullable = true,
            IsPrimaryKey = false
        });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Users" };
        tableTarget.AddColumn(new ColumnSchema
        {
            Name = "Email",
            Type = StandardType.String,
            Length = 150,
            IsNullable = false,
            IsPrimaryKey = true
        });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var colDiff = diff.FindTable("Users")!.Columns.First(c => c.ColumnName == "Email");

        colDiff.Kind.Should().Be(DiffKind.Modified);
        colDiff.Changes.Should().Be(ChangeDetail.LengthChanged | ChangeDetail.NullabilityChanged | ChangeDetail.KeyStatusChanged);
    }

    [Fact]
    public void Calculate_ForeignKeyAddedModifiedDeleted_ShouldTrackAccurately()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var tableSource = new TableSchema { Name = "Orders" };
        tableSource.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_Users",
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "UserId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });
        tableSource.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_OldTable",
            PrincipalTable = "OldTable",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "OldId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });
        source.AddTable(tableSource);

        var tableTarget = new TableSchema { Name = "Orders" };
        // Modified cardinality
        tableTarget.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_Users",
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "UserId",
            Cardinality = ForeignKeyCardinality.OneToOne
        });
        // Added new FK
        tableTarget.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_Stores",
            PrincipalTable = "Stores",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "StoreId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });
        target.AddTable(tableTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var tableDiff = diff.FindTable("Orders");

        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Modified);
        tableDiff.AddedForeignKeys.Should().HaveCount(1);
        tableDiff.AddedForeignKeys.First().ConstraintName.Should().Be("FK_Orders_Stores");

        tableDiff.DeletedForeignKeys.Should().HaveCount(1);
        tableDiff.DeletedForeignKeys.First().ConstraintName.Should().Be("FK_Orders_OldTable");

        tableDiff.ModifiedForeignKeys.Should().HaveCount(1);
        var modifiedFk = tableDiff.ModifiedForeignKeys.First();
        modifiedFk.ConstraintName.Should().Be("FK_Orders_Users");
        modifiedFk.CardinalityChanged.Should().BeTrue();
    }

    [Fact]
    public void Calculate_ForeignKeyMatchingWithoutConstraintName_ShouldMatchByRelations()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var sourceTable = new TableSchema { Name = "Orders" };
        sourceTable.AddForeignKey(new ForeignKeySchema
        {
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "Orders",
            DependentColumn = "UserId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });
        source.AddTable(sourceTable);

        var targetTable = new TableSchema { Name = "Orders" };
        targetTable.AddForeignKey(new ForeignKeySchema
        {
            PrincipalTable = "users",
            PrincipalColumn = "id",
            DependentTable = "orders",
            DependentColumn = "userid",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });
        target.AddTable(targetTable);

        var diff = SchemaDiffCalculator.Calculate(source, target);
        var tableDiff = diff.FindTable("Orders");

        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Unchanged);
        tableDiff.ForeignKeys.Should().HaveCount(1);
        tableDiff.ForeignKeys[0].Kind.Should().Be(DiffKind.Unchanged);
    }

    [Fact]
    public void Calculate_CaseInsensitiveMatching_ShouldMatchTablesAndColumnsCorrectly()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        var sourceTable = new TableSchema { Name = "users", Schema = "dbo" };
        sourceTable.AddColumn(new ColumnSchema { Name = "id", Type = StandardType.Int, IsPrimaryKey = true });
        sourceTable.AddColumn(new ColumnSchema { Name = "email_address", Type = StandardType.String, Length = 100 });
        source.AddTable(sourceTable);

        var targetTable = new TableSchema { Name = "USERS", Schema = "DBO" };
        targetTable.AddColumn(new ColumnSchema { Name = "ID", Type = StandardType.Int, IsPrimaryKey = true });
        targetTable.AddColumn(new ColumnSchema { Name = "EMAIL_ADDRESS", Type = StandardType.String, Length = 100 });
        target.AddTable(targetTable);

        var diff = SchemaDiffCalculator.Calculate(source, target);

        diff.HasChanges.Should().BeFalse();
        diff.Tables.Should().HaveCount(1);
        var tableDiff = diff.FindTable("Users");
        tableDiff.Should().NotBeNull();
        tableDiff!.Kind.Should().Be(DiffKind.Unchanged);
        tableDiff.Columns.Should().HaveCount(2);
        tableDiff.Columns.Should().OnlyContain(c => c.Kind == DiffKind.Unchanged);
    }

    [Fact]
    public void Calculate_ComplexMultiTableDiff_ShouldAccuratelyCategorizeAllChanges()
    {
        var source = new DatabaseSchema();
        var target = new DatabaseSchema();

        // 1. Unchanged table
        var usersSource = new TableSchema { Name = "Users" };
        usersSource.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        source.AddTable(usersSource);

        var usersTarget = new TableSchema { Name = "Users" };
        usersTarget.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        target.AddTable(usersTarget);

        // 2. Deleted table
        var oldAuditSource = new TableSchema { Name = "OldAudit" };
        oldAuditSource.AddColumn(new ColumnSchema { Name = "AuditId", Type = StandardType.BigInt, IsPrimaryKey = true });
        source.AddTable(oldAuditSource);

        // 3. Added table
        var paymentsTarget = new TableSchema { Name = "Payments" };
        paymentsTarget.AddColumn(new ColumnSchema { Name = "PaymentId", Type = StandardType.Guid, IsPrimaryKey = true });
        paymentsTarget.AddColumn(new ColumnSchema { Name = "Amount", Type = StandardType.Decimal, Precision = 18, Scale = 2 });
        target.AddTable(paymentsTarget);

        // 4. Modified table
        var ordersSource = new TableSchema { Name = "Orders" };
        ordersSource.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        ordersSource.AddColumn(new ColumnSchema { Name = "OldCol", Type = StandardType.String });
        ordersSource.AddColumn(new ColumnSchema { Name = "Total", Type = StandardType.Float });
        source.AddTable(ordersSource);

        var ordersTarget = new TableSchema { Name = "Orders" };
        ordersTarget.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        ordersTarget.AddColumn(new ColumnSchema { Name = "NewCol", Type = StandardType.String });
        ordersTarget.AddColumn(new ColumnSchema { Name = "Total", Type = StandardType.Decimal, Precision = 18, Scale = 2 });
        target.AddTable(ordersTarget);

        var diff = SchemaDiffCalculator.Calculate(source, target);

        diff.HasChanges.Should().BeTrue();
        diff.Tables.Should().HaveCount(4);
        diff.AddedTables.Should().ContainSingle(t => t.TableName == "Payments");
        diff.DeletedTables.Should().ContainSingle(t => t.TableName == "OldAudit");
        diff.UnchangedTables.Should().ContainSingle(t => t.TableName == "Users");
        diff.ModifiedTables.Should().ContainSingle(t => t.TableName == "Orders");

        var ordersDiff = diff.FindTable("Orders")!;
        ordersDiff.AddedColumns.Should().ContainSingle(c => c.ColumnName == "NewCol");
        ordersDiff.DeletedColumns.Should().ContainSingle(c => c.ColumnName == "OldCol");
        ordersDiff.ModifiedColumns.Should().ContainSingle(c => c.ColumnName == "Total");
    }
}
