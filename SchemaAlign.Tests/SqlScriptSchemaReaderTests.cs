using SchemaAlign.Models;
using SchemaAlign.Readers.SqlServer;
using Xunit;

namespace SchemaAlign.Tests;

public class SqlScriptSchemaReaderTests
{
    [Fact]
    public void Read_SimpleCreateTable_ParsesColumnsAndTypes()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblAlpha] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(50) NOT NULL,
    [Description] NVARCHAR(MAX) NULL,
    [IsActive] BIT NOT NULL,
    [Price] DECIMAL(18, 2) NULL,
    [CreatedAt] DATETIME2(7) NOT NULL,
    CONSTRAINT [PK_TblAlpha] PRIMARY KEY ([Id])
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblAlpha");
        table.Should().NotBeNull();
        table!.Schema.Should().Be("dbo");
        table.Columns.Should().HaveCount(6);

        var idCol = table.FindColumn("Id");
        idCol.Should().NotBeNull();
        idCol!.Type.Should().Be(StandardType.Int);
        idCol.IsPrimaryKey.Should().BeTrue();
        idCol.IsIdentity.Should().BeTrue();
        idCol.IsNullable.Should().BeFalse();

        var nameCol = table.FindColumn("Name");
        nameCol.Should().NotBeNull();
        nameCol!.Type.Should().Be(StandardType.String);
        nameCol.Length.Should().Be(50);
        nameCol.IsNullable.Should().BeFalse();

        var descCol = table.FindColumn("Description");
        descCol.Should().NotBeNull();
        descCol!.Type.Should().Be(StandardType.String);
        descCol.Length.Should().Be(-1);
        descCol.IsNullable.Should().BeTrue();

        var activeCol = table.FindColumn("IsActive");
        activeCol.Should().NotBeNull();
        activeCol!.Type.Should().Be(StandardType.Boolean);
        activeCol.IsNullable.Should().BeFalse();

        var priceCol = table.FindColumn("Price");
        priceCol.Should().NotBeNull();
        priceCol!.Type.Should().Be(StandardType.Decimal);
        priceCol.Precision.Should().Be(18);
        priceCol.Scale.Should().Be(2);
        priceCol.IsNullable.Should().BeTrue();

        var createdCol = table.FindColumn("CreatedAt");
        createdCol.Should().NotBeNull();
        createdCol!.Type.Should().Be(StandardType.DateTime);
        createdCol.Precision.Should().Be(7);
        createdCol.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Read_InlineConstraints_ParsesPrimaryKeyAndIdentityAndDefault()
    {
        // Arrange
        var sql = @"
CREATE TABLE TblBeta (
    ColKey INT PRIMARY KEY IDENTITY,
    ColText VARCHAR(100) NOT NULL DEFAULT ('sample_default'),
    ColGuid UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID()
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblBeta");
        table.Should().NotBeNull();
        table!.Schema.Should().Be("dbo");

        var keyCol = table.FindColumn("ColKey");
        keyCol.Should().NotBeNull();
        keyCol!.IsPrimaryKey.Should().BeTrue();
        keyCol.IsIdentity.Should().BeTrue();
        keyCol.Type.Should().Be(StandardType.Int);

        var textCol = table.FindColumn("ColText");
        textCol.Should().NotBeNull();
        textCol!.Type.Should().Be(StandardType.String);
        textCol.Length.Should().Be(100);
        textCol.IsNullable.Should().BeFalse();
        textCol.DefaultValue.Should().Be("('sample_default')");

        var guidCol = table.FindColumn("ColGuid");
        guidCol.Should().NotBeNull();
        guidCol!.Type.Should().Be(StandardType.Guid);
        guidCol.DefaultValue.Should().Be("NEWID()");
    }

    [Fact]
    public void Read_CompositePrimaryKey_ParsesBothColumnsAsPK()
    {
        // Arrange
        var sql = @"
CREATE TABLE [custom].[TblComposite] (
    [TenantId] INT NOT NULL,
    [ItemId] INT NOT NULL,
    [Value] NVARCHAR(100) NULL,
    CONSTRAINT [PK_TblComposite] PRIMARY KEY CLUSTERED ([TenantId] ASC, [ItemId] ASC)
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblComposite");
        table.Should().NotBeNull();
        table!.Schema.Should().Be("custom");

        table.PrimaryKeys.Should().BeEquivalentTo(new[] { "TenantId", "ItemId" });
        table.FindColumn("TenantId")!.IsPrimaryKey.Should().BeTrue();
        table.FindColumn("ItemId")!.IsPrimaryKey.Should().BeTrue();
        table.FindColumn("Value")!.IsPrimaryKey.Should().BeFalse();
    }

    [Fact]
    public void Read_TableConstraintForeignKey_ParsesForeignKey()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblParent] (
    [ParentId] INT PRIMARY KEY
);

CREATE TABLE [dbo].[TblChild] (
    [ChildId] INT PRIMARY KEY,
    [ParentRefId] INT NOT NULL,
    CONSTRAINT [FK_TblChild_TblParent] FOREIGN KEY ([ParentRefId]) REFERENCES [dbo].[TblParent]([ParentId])
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var childTable = schema.FindTable("TblChild");
        childTable.Should().NotBeNull();
        childTable!.ForeignKeys.Should().HaveCount(1);

        var fk = childTable.ForeignKeys[0];
        fk.ConstraintName.Should().Be("FK_TblChild_TblParent");
        fk.DependentTable.Should().Be("TblChild");
        fk.DependentColumn.Should().Be("ParentRefId");
        fk.PrincipalTable.Should().Be("TblParent");
        fk.PrincipalColumn.Should().Be("ParentId");
    }

    [Fact]
    public void Read_InlineForeignKey_ParsesForeignKey()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblParent] (
    [Id] INT PRIMARY KEY
);

CREATE TABLE [dbo].[TblChild] (
    [Id] INT PRIMARY KEY,
    [ParentId] INT REFERENCES [dbo].[TblParent]([Id])
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var childTable = schema.FindTable("TblChild");
        childTable.Should().NotBeNull();
        childTable!.ForeignKeys.Should().HaveCount(1);

        var fk = childTable.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("TblParent");
        fk.PrincipalColumn.Should().Be("Id");
        fk.DependentTable.Should().Be("TblChild");
        fk.DependentColumn.Should().Be("ParentId");
    }

    [Fact]
    public void Read_AlterTableAddForeignKey_ParsesForeignKey()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblOrder] (
    [OrderId] INT PRIMARY KEY
);

CREATE TABLE [dbo].[TblOrderItem] (
    [ItemId] INT PRIMARY KEY,
    [OrderId] INT NOT NULL
);

ALTER TABLE [dbo].[TblOrderItem] WITH CHECK ADD CONSTRAINT [FK_TblOrderItem_TblOrder] FOREIGN KEY([OrderId])
REFERENCES [dbo].[TblOrder] ([OrderId])
ON DELETE CASCADE;";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var itemTable = schema.FindTable("TblOrderItem");
        itemTable.Should().NotBeNull();
        itemTable!.ForeignKeys.Should().HaveCount(1);

        var fk = itemTable.ForeignKeys[0];
        fk.ConstraintName.Should().Be("FK_TblOrderItem_TblOrder");
        fk.DependentTable.Should().Be("TblOrderItem");
        fk.DependentColumn.Should().Be("OrderId");
        fk.PrincipalTable.Should().Be("TblOrder");
        fk.PrincipalColumn.Should().Be("OrderId");
    }

    [Fact]
    public void Read_AlterTableAddColumn_ParsesNewColumn()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblAlpha] (
    [Id] INT PRIMARY KEY
);

ALTER TABLE [dbo].[TblAlpha] ADD [ExtraNote] NVARCHAR(200) NULL, [Flag] BIT NOT NULL DEFAULT (1);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblAlpha");
        table.Should().NotBeNull();
        table!.Columns.Should().HaveCount(3);

        var noteCol = table.FindColumn("ExtraNote");
        noteCol.Should().NotBeNull();
        noteCol!.Type.Should().Be(StandardType.String);
        noteCol.Length.Should().Be(200);
        noteCol.IsNullable.Should().BeTrue();

        var flagCol = table.FindColumn("Flag");
        flagCol.Should().NotBeNull();
        flagCol!.Type.Should().Be(StandardType.Boolean);
        flagCol.IsNullable.Should().BeFalse();
        flagCol.DefaultValue.Should().Be("(1)");
    }

    [Fact]
    public void Read_ExtendedProperties_ParsesTableAndColumnComments()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblDocument] (
    [DocId] INT PRIMARY KEY,
    [Title] NVARCHAR(100) NOT NULL
);
GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Document storage table' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TblDocument';
GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'The primary key for document' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'TblDocument', @level2type=N'COLUMN',@level2name=N'DocId';
GO";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblDocument");
        table.Should().NotBeNull();
        table!.Comment.Should().Be("Document storage table");

        var docIdCol = table.FindColumn("DocId");
        docIdCol.Should().NotBeNull();
        docIdCol!.Comment.Should().Be("The primary key for document");
    }

    [Fact]
    public void Read_ComplexScriptWithGoAndComments_ParsesCorrectly()
    {
        // Arrange
        var sql = @"
/*
   Multi-line header comment describing database structure
*/
-- Creating alpha table
CREATE TABLE [dbo].[TblAlpha] (
    [Id] INT IDENTITY(1, 1) NOT NULL,
    -- Column comment here
    [Code] VARCHAR(20) NOT NULL,
    CONSTRAINT [PK_TblAlpha] PRIMARY KEY ([Id])
);
GO

/* Secondary table */
CREATE TABLE [sales].[TblBeta] (
    [BetaId] BIGINT PRIMARY KEY,
    [AlphaId] INT NOT NULL,
    [Amount] MONEY NULL,
    [BinaryBlob] VARBINARY(MAX) NULL,
    [TimestampVal] ROWVERSION
);
GO

ALTER TABLE [sales].[TblBeta] ADD CONSTRAINT [FK_Beta_Alpha] FOREIGN KEY ([AlphaId]) REFERENCES [dbo].[TblAlpha] ([Id]);
GO";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        schema.Tables.Should().HaveCount(2);

        var alpha = schema.FindTable("TblAlpha");
        alpha.Should().NotBeNull();
        alpha!.Schema.Should().Be("dbo");
        alpha.FindColumn("Id")!.IsPrimaryKey.Should().BeTrue();
        alpha.FindColumn("Code")!.Type.Should().Be(StandardType.String);
        alpha.FindColumn("Code")!.Length.Should().Be(20);

        var beta = schema.FindTable("TblBeta");
        beta.Should().NotBeNull();
        beta!.Schema.Should().Be("sales");
        beta.FindColumn("BetaId")!.Type.Should().Be(StandardType.BigInt);
        beta.FindColumn("Amount")!.Type.Should().Be(StandardType.Decimal);
        beta.FindColumn("BinaryBlob")!.Type.Should().Be(StandardType.ByteArray);
        beta.FindColumn("BinaryBlob")!.Length.Should().Be(-1);
        beta.FindColumn("TimestampVal")!.Type.Should().Be(StandardType.ByteArray);

        beta.ForeignKeys.Should().HaveCount(1);
        beta.ForeignKeys[0].PrincipalTable.Should().Be("TblAlpha");
        beta.ForeignKeys[0].DependentColumn.Should().Be("AlphaId");
    }

    [Fact]
    public void Read_VariousSqlDataTypes_MapsToStandardTypes()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblDataTypes] (
    [ColInt] INT,
    [ColBigInt] BIGINT,
    [ColSmallInt] SMALLINT,
    [ColTinyInt] TINYINT,
    [ColBit] BIT,
    [ColDecimal] DECIMAL(10, 4),
    [ColNumeric] NUMERIC(12, 2),
    [ColMoney] MONEY,
    [ColFloat] FLOAT(53),
    [ColReal] REAL,
    [ColDateTime] DATETIME,
    [ColDateTime2] DATETIME2(3),
    [ColDateTimeOffset] DATETIMEOFFSET(7),
    [ColDate] DATE,
    [ColTime] TIME(4),
    [ColGuid] UNIQUEIDENTIFIER,
    [ColVarBinary] VARBINARY(512),
    [ColVarBinaryMax] VARBINARY(MAX),
    [ColNVarChar] NVARCHAR(250),
    [ColNVarCharMax] NVARCHAR(MAX),
    [ColVarChar] VARCHAR(100),
    [ColChar] CHAR(10),
    [ColNChar] NCHAR(5),
    [ColXml] XML
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblDataTypes");
        table.Should().NotBeNull();

        table!.FindColumn("ColInt")!.Type.Should().Be(StandardType.Int);
        table.FindColumn("ColBigInt")!.Type.Should().Be(StandardType.BigInt);
        table.FindColumn("ColSmallInt")!.Type.Should().Be(StandardType.SmallInt);
        table.FindColumn("ColTinyInt")!.Type.Should().Be(StandardType.TinyInt);
        table.FindColumn("ColBit")!.Type.Should().Be(StandardType.Boolean);
        table.FindColumn("ColDecimal")!.Type.Should().Be(StandardType.Decimal);
        table.FindColumn("ColDecimal")!.Precision.Should().Be(10);
        table.FindColumn("ColDecimal")!.Scale.Should().Be(4);
        table.FindColumn("ColNumeric")!.Type.Should().Be(StandardType.Decimal);
        table.FindColumn("ColNumeric")!.Precision.Should().Be(12);
        table.FindColumn("ColNumeric")!.Scale.Should().Be(2);
        table.FindColumn("ColMoney")!.Type.Should().Be(StandardType.Decimal);
        table.FindColumn("ColFloat")!.Type.Should().Be(StandardType.Double);
        table.FindColumn("ColReal")!.Type.Should().Be(StandardType.Float);
        table.FindColumn("ColDateTime")!.Type.Should().Be(StandardType.DateTime);
        table.FindColumn("ColDateTime2")!.Type.Should().Be(StandardType.DateTime);
        table.FindColumn("ColDateTime2")!.Precision.Should().Be(3);
        table.FindColumn("ColDateTimeOffset")!.Type.Should().Be(StandardType.DateTimeOffset);
        table.FindColumn("ColDateTimeOffset")!.Precision.Should().Be(7);
        table.FindColumn("ColDate")!.Type.Should().Be(StandardType.Date);
        table.FindColumn("ColTime")!.Type.Should().Be(StandardType.Time);
        table.FindColumn("ColTime")!.Precision.Should().Be(4);
        table.FindColumn("ColGuid")!.Type.Should().Be(StandardType.Guid);
        table.FindColumn("ColVarBinary")!.Type.Should().Be(StandardType.ByteArray);
        table.FindColumn("ColVarBinary")!.Length.Should().Be(512);
        table.FindColumn("ColVarBinaryMax")!.Type.Should().Be(StandardType.ByteArray);
        table.FindColumn("ColVarBinaryMax")!.Length.Should().Be(-1);
        table.FindColumn("ColNVarChar")!.Type.Should().Be(StandardType.String);
        table.FindColumn("ColNVarChar")!.Length.Should().Be(250);
        table.FindColumn("ColNVarCharMax")!.Type.Should().Be(StandardType.String);
        table.FindColumn("ColNVarCharMax")!.Length.Should().Be(-1);
        table.FindColumn("ColVarChar")!.Type.Should().Be(StandardType.String);
        table.FindColumn("ColVarChar")!.Length.Should().Be(100);
        table.FindColumn("ColChar")!.Type.Should().Be(StandardType.String);
        table.FindColumn("ColChar")!.Length.Should().Be(10);
        table.FindColumn("ColNChar")!.Type.Should().Be(StandardType.String);
        table.FindColumn("ColNChar")!.Length.Should().Be(5);
        table.FindColumn("ColXml")!.Type.Should().Be(StandardType.String);
    }

    [Fact]
    public void ReadFileAndDirectory_ReadsSchemaFromFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlignSqlTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var file1 = Path.Combine(tempDir, "01_Alpha.sql");
            var file2 = Path.Combine(tempDir, "02_Beta.sql");

            File.WriteAllText(file1, "CREATE TABLE [dbo].[TblAlpha] ( [Id] INT PRIMARY KEY );");
            File.WriteAllText(file2, "CREATE TABLE [dbo].[TblBeta] ( [Id] INT PRIMARY KEY, [AlphaId] INT REFERENCES [dbo].[TblAlpha]([Id]) );");

            var reader = new SqlScriptSchemaReader();

            // Act - single file
            var schema1 = reader.ReadFile(file1);
            schema1.Tables.Should().ContainKey("TblAlpha");

            // Act - directory
            var schemaDir = reader.ReadDirectory(tempDir);
            schemaDir.Tables.Should().ContainKey("TblAlpha");
            schemaDir.Tables.Should().ContainKey("TblBeta");
            schemaDir.FindTable("TblBeta")!.ForeignKeys.Should().HaveCount(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Read_EmptyOrWhitespace_ReturnsEmptySchema()
    {
        var reader = new SqlScriptSchemaReader();
        var schema = reader.Read("   ");
        schema.Tables.Should().BeEmpty();
    }

    [Fact]
    public void Read_MultipleAlterTablesInSingleBatchWithoutGo_ParsesAllStatements()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblParent] (
    [Id] INT PRIMARY KEY
);
CREATE TABLE [dbo].[TblChild] (
    [Id] INT PRIMARY KEY,
    [ParentId] INT NOT NULL
);
CREATE TABLE [dbo].[TblOther] (
    [Id] INT PRIMARY KEY
);

ALTER TABLE [dbo].[TblChild] WITH CHECK ADD CONSTRAINT [FK_TblChild_TblParent] FOREIGN KEY([ParentId]) REFERENCES [dbo].[TblParent] ([Id]);
ALTER TABLE [dbo].[TblOther] ADD [NewCol] NVARCHAR(100) NULL;
";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var childTable = schema.FindTable("TblChild");
        childTable.Should().NotBeNull();
        childTable!.ForeignKeys.Should().HaveCount(1);
        childTable.ForeignKeys[0].ConstraintName.Should().Be("FK_TblChild_TblParent");

        var otherTable = schema.FindTable("TblOther");
        otherTable.Should().NotBeNull();
        otherTable!.Columns.Should().HaveCount(2);
        var newCol = otherTable.FindColumn("NewCol");
        newCol.Should().NotBeNull();
        newCol!.Type.Should().Be(StandardType.String);
        newCol.Length.Should().Be(100);
    }

    [Fact]
    public void Read_MultipleExtendedPropertiesInSingleBatchWithoutGo_ParsesAllComments()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblCustomer] (
    [CustomerId] INT PRIMARY KEY,
    [Email] NVARCHAR(100) NOT NULL
);

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer entity table', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'TblCustomer';
EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Customer primary email address', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'TblCustomer', @level2type=N'COLUMN', @level2name=N'Email';
";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblCustomer");
        table.Should().NotBeNull();
        table!.Comment.Should().Be("Customer entity table");

        var emailCol = table.FindColumn("Email");
        emailCol.Should().NotBeNull();
        emailCol!.Comment.Should().Be("Customer primary email address");
    }

    [Fact]
    public void Read_BracketedDataTypes_MapsToStandardTypesWithPrecisionAndLength()
    {
        // Arrange
        var sql = @"
CREATE TABLE [dbo].[TblSsmsTypes] (
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Name] [nvarchar](50) NOT NULL,
    [Price] [decimal](18, 2) NULL,
    [CreatedAt] [datetime2](7) NOT NULL,
    [Payload] [varbinary](max) NULL,
    [Note] [sys].[nvarchar](100) NULL,
    CONSTRAINT [PK_TblSsmsTypes] PRIMARY KEY ([Id])
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var table = schema.FindTable("TblSsmsTypes");
        table.Should().NotBeNull();

        var idCol = table.FindColumn("Id");
        idCol.Should().NotBeNull();
        idCol!.Type.Should().Be(StandardType.Int);

        var nameCol = table.FindColumn("Name");
        nameCol.Should().NotBeNull();
        nameCol!.Type.Should().Be(StandardType.String);
        nameCol.Length.Should().Be(50);

        var priceCol = table.FindColumn("Price");
        priceCol.Should().NotBeNull();
        priceCol!.Type.Should().Be(StandardType.Decimal);
        priceCol.Precision.Should().Be(18);
        priceCol.Scale.Should().Be(2);

        var createdCol = table.FindColumn("CreatedAt");
        createdCol.Should().NotBeNull();
        createdCol!.Type.Should().Be(StandardType.DateTime);
        createdCol.Precision.Should().Be(7);

        var payloadCol = table.FindColumn("Payload");
        payloadCol.Should().NotBeNull();
        payloadCol!.Type.Should().Be(StandardType.ByteArray);
        payloadCol.Length.Should().Be(-1);

        var noteCol = table.FindColumn("Note");
        noteCol.Should().NotBeNull();
        noteCol!.Type.Should().Be(StandardType.String);
        noteCol.Length.Should().Be(100);
    }

    [Fact]
    public void Read_MultiPartQualifiedTableNames_CorrectlyExtractsSchemaAndTableName()
    {
        // Arrange
        var sql = @"
CREATE TABLE [MyDatabase].[dbo].[Orders] (
    [OrderId] INT PRIMARY KEY
);

CREATE TABLE [MyServer].[MyDatabase].[sales].[OrderItems] (
    [ItemId] INT PRIMARY KEY,
    [OrderId] INT NOT NULL,
    CONSTRAINT [FK_OrderItems_Orders] FOREIGN KEY ([OrderId]) REFERENCES [MyDatabase].[dbo].[Orders] ([OrderId])
);";
        var reader = new SqlScriptSchemaReader();

        // Act
        var schema = reader.Read(sql);

        // Assert
        var ordersTable = schema.FindTable("Orders");
        ordersTable.Should().NotBeNull();
        ordersTable!.Schema.Should().Be("dbo");

        var itemsTable = schema.FindTable("OrderItems");
        itemsTable.Should().NotBeNull();
        itemsTable!.Schema.Should().Be("sales");
        itemsTable.ForeignKeys.Should().HaveCount(1);
        itemsTable.ForeignKeys[0].PrincipalTable.Should().Be("Orders");
    }
}
