using SchemaAlign.Models;
using SchemaAlign.Readers;
using SchemaAlign.Readers.Mermaid;
using Xunit;


namespace SchemaAlign.Tests;

public class MermaidSchemaReaderTests
{
    private readonly MermaidSchemaReader _reader = new();

    [Fact]
    public void Implements_ISchemaReader_Interface()
    {
        _reader.Should().BeAssignableTo<ISchemaReader>();
    }

    [Fact]
    public void Parse_BasicEntity_ShouldPopulateTableAndColumns()
    {
        var mermaid = """
            erDiagram
                Users {
                    int Id PK
                    string Email
                    string Name
                    datetime CreatedAt
                    bool IsActive
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("Users");
        var table = schema.Tables["Users"];
        table.Name.Should().Be("Users");
        table.Schema.Should().Be("dbo");
        table.Columns.Should().HaveCount(5);

        table.Columns["Id"].Type.Should().Be(StandardType.Int);
        table.Columns["Id"].IsPrimaryKey.Should().BeTrue();
        table.Columns["Id"].IsNullable.Should().BeFalse();

        table.Columns["Email"].Type.Should().Be(StandardType.String);
        table.Columns["Name"].Type.Should().Be(StandardType.String);
        table.Columns["CreatedAt"].Type.Should().Be(StandardType.DateTime);
        table.Columns["IsActive"].Type.Should().Be(StandardType.Boolean);
    }

    [Fact]
    public void Parse_CompositePrimaryKeys_ShouldMarkAllPrimaryKeys()
    {
        var mermaid = """
            erDiagram
                OrderItems {
                    int OrderId PK
                    int ProductId PK
                    int Quantity
                    decimal Price
                }
            """;

        var schema = _reader.Read(mermaid);

        var table = schema.Tables["OrderItems"];
        table.PrimaryKeys.Should().BeEquivalentTo(new[] { "OrderId", "ProductId" });
        table.Columns["OrderId"].IsPrimaryKey.Should().BeTrue();
        table.Columns["ProductId"].IsPrimaryKey.Should().BeTrue();
        table.Columns["Quantity"].IsPrimaryKey.Should().BeFalse();
    }

    [Fact]
    public void Parse_ColumnConstraints_ShouldHandleMultipleFlags()
    {
        var mermaid = """
            erDiagram
                UserRoles {
                    int UserId PK, FK
                    int RoleId PK,FK
                    string AssignedBy FK
                }
            """;

        var schema = _reader.Read(mermaid);

        var table = schema.Tables["UserRoles"];
        table.Columns["UserId"].IsPrimaryKey.Should().BeTrue();
        table.Columns["RoleId"].IsPrimaryKey.Should().BeTrue();
        table.Columns["AssignedBy"].IsPrimaryKey.Should().BeFalse();
    }

    [Fact]
    public void Parse_CommentsAndLengths_ShouldExtractDimensionsAndDescriptions()
    {
        var mermaid = """
            erDiagram
                Products {
                    int Id PK "Identifier"
                    string Sku "50"
                    string Title "100, Product display title"
                    string Description "max, Extended description"
                    decimal Price "18,2, Unit price"
                    decimal Weight "10,3"
                }
            """;

        var schema = _reader.Read(mermaid);

        var table = schema.Tables["Products"];
        table.Columns["Id"].Comment.Should().Be("Identifier");

        table.Columns["Sku"].Length.Should().Be(50);
        table.Columns["Sku"].Comment.Should().BeNull();

        table.Columns["Title"].Length.Should().Be(100);
        table.Columns["Title"].Comment.Should().Be("Product display title");

        table.Columns["Description"].Length.Should().Be(-1);
        table.Columns["Description"].Comment.Should().Be("Extended description");

        table.Columns["Price"].Type.Should().Be(StandardType.Decimal);
        table.Columns["Price"].Precision.Should().Be(18);
        table.Columns["Price"].Scale.Should().Be(2);
        table.Columns["Price"].Comment.Should().Be("Unit price");

        table.Columns["Weight"].Precision.Should().Be(10);
        table.Columns["Weight"].Scale.Should().Be(3);
    }

    [Fact]
    public void Parse_TypeDimensionSyntax_ShouldExtractLengthAndPrecision()
    {
        var mermaid = """
            erDiagram
                Customers {
                    int Id PK
                    varchar(128) Code
                    nvarchar(max) Bio
                    decimal(12,4) Balance
                }
            """;

        var schema = _reader.Read(mermaid);

        var table = schema.Tables["Customers"];
        table.Columns["Code"].Length.Should().Be(128);
        table.Columns["Code"].Type.Should().Be(StandardType.String);

        table.Columns["Bio"].Length.Should().Be(-1);
        table.Columns["Bio"].Type.Should().Be(StandardType.String);

        table.Columns["Balance"].Type.Should().Be(StandardType.Decimal);
        table.Columns["Balance"].Precision.Should().Be(12);
        table.Columns["Balance"].Scale.Should().Be(4);
    }

    [Fact]
    public void Parse_NullableTypes_ShouldSetIsNullable()
    {
        var mermaid = """
            erDiagram
                Employees {
                    int Id PK
                    string? MiddleName
                    int? ManagerId FK
                    datetime BirthDate "nullable"
                }
            """;

        var schema = _reader.Read(mermaid);

        var table = schema.Tables["Employees"];
        table.Columns["Id"].IsNullable.Should().BeFalse();
        table.Columns["MiddleName"].IsNullable.Should().BeTrue();
        table.Columns["ManagerId"].IsNullable.Should().BeTrue();
        table.Columns["BirthDate"].IsNullable.Should().BeTrue();
    }

    [Fact]
    public void Parse_SchemaQualifiedTableNames_ShouldSetSchemaAndName()
    {
        var mermaid = """
            erDiagram
                sales.Orders {
                    int Id PK
                    int CustomerId FK
                }
                [inventory].[Products] {
                    int Id PK
                    string Name
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("Orders");
        schema.Tables["Orders"].Schema.Should().Be("sales");

        schema.Tables.Should().ContainKey("Products");
        schema.Tables["Products"].Schema.Should().Be("inventory");
    }

    [Fact]
    public void Parse_Relationships_OneToMany_ShouldCreateForeignKey()
    {
        var mermaid = """
            erDiagram
                Customers {
                    int Id PK
                    string Name
                }
                Orders {
                    int Id PK
                    int CustomerId FK
                    decimal Total
                }
                Customers ||--o{ Orders : "places"
            """;

        var schema = _reader.Read(mermaid);

        var ordersTable = schema.Tables["Orders"];
        ordersTable.ForeignKeys.Should().HaveCount(1);

        var fk = ordersTable.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("Customers");
        fk.PrincipalColumn.Should().Be("Id");
        fk.DependentTable.Should().Be("Orders");
        fk.DependentColumn.Should().Be("CustomerId");
        fk.Cardinality.Should().Be(ForeignKeyCardinality.OneToMany);
    }

    [Fact]
    public void Parse_Relationships_ManyToOne_ShouldCreateForeignKey()
    {
        var mermaid = """
            erDiagram
                Orders {
                    int Id PK
                    int CustomerId FK
                }
                Customers {
                    int Id PK
                }
                Orders }o--|| Customers : "belongs to"
            """;

        var schema = _reader.Read(mermaid);

        var ordersTable = schema.Tables["Orders"];
        ordersTable.ForeignKeys.Should().HaveCount(1);

        var fk = ordersTable.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("Customers");
        fk.PrincipalColumn.Should().Be("Id");
        fk.DependentTable.Should().Be("Orders");
        fk.DependentColumn.Should().Be("CustomerId");
        fk.Cardinality.Should().Be(ForeignKeyCardinality.ManyToOne);
    }

    [Fact]
    public void Parse_Relationships_OneToOne_ShouldCreateForeignKey()
    {
        var mermaid = """
            erDiagram
                Users {
                    int Id PK
                }
                UserProfiles {
                    int UserId PK, FK
                    string Bio
                }
                Users ||--|| UserProfiles : "has profile"
            """;

        var schema = _reader.Read(mermaid);

        var profileTable = schema.Tables["UserProfiles"];
        profileTable.ForeignKeys.Should().HaveCount(1);

        var fk = profileTable.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("Users");
        fk.PrincipalColumn.Should().Be("Id");
        fk.DependentTable.Should().Be("UserProfiles");
        fk.DependentColumn.Should().Be("UserId");
        fk.Cardinality.Should().Be(ForeignKeyCardinality.OneToOne);
    }

    [Fact]
    public void Parse_Relationships_ManyToMany_ShouldCreateForeignKey()
    {
        var mermaid = """
            erDiagram
                Students }o--o{ Courses : "enrolls"
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("Students");
        schema.Tables.Should().ContainKey("Courses");

        var coursesTable = schema.Tables["Courses"];
        coursesTable.ForeignKeys.Should().HaveCount(1);
        coursesTable.ForeignKeys[0].Cardinality.Should().Be(ForeignKeyCardinality.ManyToMany);
        coursesTable.ForeignKeys[0].PrincipalTable.Should().Be("Students");
        coursesTable.ForeignKeys[0].DependentTable.Should().Be("Courses");
    }

    [Fact]
    public void Parse_FormattingVariations_ShouldParseCleanly()
    {
        var mermaid = """
            ---
            title: Complex Diagram Title
            ---
            %% This is a top-level comment
            erDiagram
                %% In-diagram comment
                
                CUSTOMER {
                    int id PK
                    string name
                }
                
                ORDER    {
                    int id PK
                    int customer_id FK
                }

                %% Relationship with dashed line and unquoted label
                CUSTOMER ||..|{ ORDER : places_order
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("CUSTOMER");
        schema.Tables.Should().ContainKey("ORDER");

        var order = schema.Tables["ORDER"];
        order.ForeignKeys.Should().HaveCount(1);
        order.ForeignKeys[0].PrincipalTable.Should().Be("CUSTOMER");
        order.ForeignKeys[0].DependentColumn.Should().Be("customer_id");
    }

    [Fact]
    public void Parse_EmptyOrWhitespaceDiagram_ShouldReturnEmptySchema()
    {
        var schema1 = _reader.Read("");
        var schema2 = _reader.Read("   \r\n\t  ");
        var schema3 = _reader.Read("erDiagram");

        schema1.Tables.Should().BeEmpty();
        schema2.Tables.Should().BeEmpty();
        schema3.Tables.Should().BeEmpty();
    }

    [Fact]
    public void ReadFile_ShouldReadAndParseMermaidFile()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = """
                erDiagram
                    Posts {
                        int Id PK
                        string Title
                    }
                """;
            File.WriteAllText(tempFile, content);

            var schema = _reader.ReadFile(tempFile);

            schema.Tables.Should().ContainKey("Posts");
            schema.Tables["Posts"].Columns["Title"].Type.Should().Be(StandardType.String);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Parse_TableWithCommentInBrackets_ShouldSetTableComment()
    {
        var mermaid = """
            erDiagram
                Users["User account details"] {
                    int Id PK
                    string Username
                }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("Users");
        schema.Tables["Users"].Comment.Should().Be("User account details");
    }

    [Fact]
    public void Parse_ZeroOrOneCardinalities_ShouldMapCorrectly()
    {
        var mermaid = """
            erDiagram
                Users |o--o| Profiles : "optional profile"
                Departments |o--o{ Employees : "has members"
            """;

        var schema = _reader.Read(mermaid);

        var profileTable = schema.Tables["Profiles"];
        profileTable.ForeignKeys.Should().HaveCount(1);
        profileTable.ForeignKeys[0].Cardinality.Should().Be(ForeignKeyCardinality.OneToOne);

        var empTable = schema.Tables["Employees"];
        empTable.ForeignKeys.Should().HaveCount(1);
        empTable.ForeignKeys[0].Cardinality.Should().Be(ForeignKeyCardinality.OneToMany);
    }

    [Fact]
    public void Parse_ChainedRelationships_ShouldPopulateAllForeignKeys()
    {
        var mermaid = """
            erDiagram
                Companies {
                    int Id PK
                    string Name
                }
                Departments {
                    int Id PK
                    int CompanyId FK
                    string Name
                }
                Employees {
                    int Id PK
                    int DepartmentId FK
                    string FullName
                }

                Companies ||--o{ Departments : "owns"
                Departments ||--o{ Employees : "employs"
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables["Departments"].ForeignKeys.Should().HaveCount(1);
        schema.Tables["Departments"].ForeignKeys[0].PrincipalTable.Should().Be("Companies");
        schema.Tables["Departments"].ForeignKeys[0].DependentColumn.Should().Be("CompanyId");

        schema.Tables["Employees"].ForeignKeys.Should().HaveCount(1);
        schema.Tables["Employees"].ForeignKeys[0].PrincipalTable.Should().Be("Departments");
        schema.Tables["Employees"].ForeignKeys[0].DependentColumn.Should().Be("DepartmentId");
    }

    [Fact]
    public void Parse_SingleLineEntityDeclaration_ShouldParseColumns()
    {
        var mermaid = """
            erDiagram
                Tags { int Id PK; string Name "50" }
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().ContainKey("Tags");
        var table = schema.Tables["Tags"];
        table.Columns.Should().HaveCount(2);
        table.Columns["Id"].IsPrimaryKey.Should().BeTrue();
        table.Columns["Name"].Length.Should().Be(50);
    }

    [Fact]
    public void Parse_MaskedDiagram_WithFrontmatterAndDiverseSqlTypes_ShouldSucceed()
    {
        var mermaid = """
            ---
            config:
              layout: elk
              theme: forest
            ---
            erDiagram

            Tenant {
                nvarchar(36) IdTenant PK
                nvarchar(40) TenantName
                bit IsActive
                datetime2(7) CreatedDate
            }

            Account {
                nvarchar(36) IdAccount PK
                nvarchar(150) DisplayName
                datetime2(7) BirthDate
                bit IsActive
                nvarchar(36) IdTenant FK
                nvarchar(max) MetadataPayload
                datetime2(7) LastSyncedDate
            }

            Subscription {
                nvarchar(36) IdSubscription PK
                nvarchar(36) IdTenant FK
                int MaxUsers
                decimal MonthlyRate
                bit IsActive
            }

            AuditEntry {
                nvarchar(36) IdAuditEntry PK
                nvarchar(36) IdAccount FK
                nvarchar(50) ActionCode
                datetime2(7) LogTime
            }

            AuditDetail {
                nvarchar(36) IdAuditDetail PK
                nvarchar(36) IdAuditEntry FK
                nvarchar(max) ErrorPayload
            }

            RefundPolicy {
                nvarchar(36) IdRefundPolicy PK
                nvarchar(36) IdTenant FK
                int MaxDays
            }

            Tenant ||--o{ Account : "1:M"
            Tenant ||--o{ Subscription : "1:M"
            Account ||--o{ AuditEntry : "1:M"
            AuditEntry ||--o{ AuditDetail : "1:M"
            Tenant ||--|| RefundPolicy : "1:1"
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().HaveCount(6);
        schema.Tables.Should().ContainKeys("Tenant", "Account", "Subscription", "AuditEntry", "AuditDetail", "RefundPolicy");

        var account = schema.Tables["Account"];
        account.Columns["IdAccount"].IsPrimaryKey.Should().BeTrue();
        account.Columns["IdAccount"].Length.Should().Be(36);
        account.Columns["BirthDate"].Type.Should().Be(StandardType.DateTime);
        account.Columns["IsActive"].Type.Should().Be(StandardType.Boolean);
        account.Columns["MetadataPayload"].Length.Should().Be(-1);

        var auditDetail = schema.Tables["AuditDetail"];
        auditDetail.Columns["ErrorPayload"].Length.Should().Be(-1);

        var refund = schema.Tables["RefundPolicy"];
        refund.ForeignKeys.Should().HaveCount(1);
        refund.ForeignKeys[0].Cardinality.Should().Be(ForeignKeyCardinality.OneToOne);
    }

    [Fact]
    public void Parse_MaskedDiagram_WithDirectivesTightFormattingAndMixedTypes_ShouldSucceed()
    {
        var mermaid = """
            ---
            config:
              layout: elk
              theme: forest
            ---
            erDiagram
                direction TB

                MediaCatalog {
                    Nvarchar(36) IdMediaCatalog PK ""
                    Nvarchar(150) Title ""
                    Nvarchar(36) IdCategory FK ""
                    Decimal WidthDimension ""
                    Decimal HeightDimension ""
                    Time MediaDuration ""
                    Bit IsAvailable ""
                    Int ReleaseYear ""
                    Nvarchar(2048) CoverUrl ""
                    Nvarchar(36) UserIn ""
                    Datetime2(7) DateIn ""
                }

                Category {
                    Nvarchar(36) IdCategory PK ""
                    Nvarchar(100) CategoryName ""
                    Bit IsActive ""
                }

                Supplier {
                    Nvarchar(36) IdSupplier PK ""
                    Nvarchar(100) SupplierName ""
                }

                MediaSupplier {
                    Nvarchar(36) IdMediaSupplier PK ""
                    Nvarchar(36) IdMediaCatalog FK ""
                    Nvarchar(36) IdSupplier FK ""
                }

                CatalogDetail {
                    nvarchar(36) IdCatalogDetail PK ""
                    nvarchar(36) IdMediaCatalog FK ""
                    nvarchar(50) DetailCode
                }

                RequestItem {
                    nvarchar(36) IdRequestItem PK ""
                    nvarchar(36) IdMediaCatalog FK ""
                    int Quantity
                }

                Category||--o{MediaCatalog:"1:M"
                Supplier||--o{MediaSupplier:"1:M"
                MediaCatalog||--o{MediaSupplier:"1:M"
                MediaCatalog||--o|CatalogDetail:"1:0"
                MediaCatalog||--|{RequestItem:"1:M"
                RequestItem}|--||MediaCatalog:"  "
                Category||--||Supplier:"1:1"
                MediaSupplier||-- o{MediaCatalog:"1:M"
                Category||--o{Supplier:""
            """;

        var schema = _reader.Read(mermaid);

        schema.Tables.Should().HaveCount(6);
        schema.Tables.Should().ContainKeys("MediaCatalog", "Category", "Supplier", "MediaSupplier", "CatalogDetail", "RequestItem");

        var media = schema.Tables["MediaCatalog"];
        media.Columns["IdMediaCatalog"].IsPrimaryKey.Should().BeTrue();
        media.Columns["IdMediaCatalog"].Length.Should().Be(36);
        media.Columns["CoverUrl"].Length.Should().Be(2048);
        media.Columns["WidthDimension"].Type.Should().Be(StandardType.Decimal);
        media.Columns["MediaDuration"].Type.Should().Be(StandardType.Time);
        media.Columns["IsAvailable"].Type.Should().Be(StandardType.Boolean);
        media.Columns["ReleaseYear"].Type.Should().Be(StandardType.Int);

        var mediaSupplier = schema.Tables["MediaSupplier"];
        mediaSupplier.ForeignKeys.Should().HaveCount(2);

        var requestItem = schema.Tables["RequestItem"];
        requestItem.ForeignKeys.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_AttributeWithForeignKeyFlag_SetsIsForeignKeyFlag()
    {
        var mermaid = """
            erDiagram
                SampleDependent {
                    int Id PK
                    string StatusCode FK
                }
            """;

        var schema = _reader.Read(mermaid);
        var table = schema.Tables["SampleDependent"];
        table.Columns["StatusCode"].IsForeignKey.Should().BeTrue();
    }

    [Fact]
    public void Parse_RelationshipWithLabel_ResolvesCustomForeignKeyColumn()
    {
        var mermaid = """
            erDiagram
                SamplePrincipal {
                    string IdPrincipalKey PK
                    string Title
                }
                SampleDependent {
                    int Id PK
                    string CustomStatusCode FK
                }
                SamplePrincipal ||--o{ SampleDependent : CustomStatusCode
            """;

        var schema = _reader.Read(mermaid);
        var dependent = schema.Tables["SampleDependent"];
        dependent.ForeignKeys.Should().HaveCount(1);
        var fk = dependent.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("SamplePrincipal");
        fk.PrincipalColumn.Should().Be("IdPrincipalKey");
        fk.DependentColumn.Should().Be("CustomStatusCode");
    }

    [Fact]
    public void Parse_RelationshipWithPrefixIdColumn_ResolvesForeignKey()
    {
        var mermaid = """
            erDiagram
                SamplePrincipalEntity {
                    string Id PK
                }
                SampleDependentEntity {
                    int Id PK
                    string IdSamplePrincipalEntity FK
                }
                SamplePrincipalEntity ||--o{ SampleDependentEntity : has
            """;

        var schema = _reader.Read(mermaid);
        var dependent = schema.Tables["SampleDependentEntity"];
        dependent.ForeignKeys.Should().HaveCount(1);
        var fk = dependent.ForeignKeys[0];
        fk.PrincipalTable.Should().Be("SamplePrincipalEntity");
        fk.DependentColumn.Should().Be("IdSamplePrincipalEntity");
    }

    [Fact]
    public void Parse_NullableColumns_SupportsQuestionMarkAndNullComments()
    {
        var mermaid = """
            erDiagram
                SampleUserTable {
                    int Id PK "NULL"
                    string MandatoryField
                    string MandatoryWithDesc "100, Required code"
                    string? NullableWithQuestionMark
                    string NullableWithNullComment "NULL"
                    string NullableWithNullableComment "nullable"
                    string NullableWithLengthAndNull "50, NULL"
                    string NullableWithLengthNullAndDesc "100, NULL, Optional display name"
                    decimal NullableWithPrecScaleAndNull "18,2, NULL, Optional rate"
                    string NullableWithNullPrefix "NULL, User bio"
                }
            """;

        var schema = _reader.Read(mermaid);
        var table = schema.Tables["SampleUserTable"];

        // PK is always mandatory
        table.Columns["Id"].IsPrimaryKey.Should().BeTrue();
        table.Columns["Id"].IsNullable.Should().BeFalse();

        // Mandatory fields
        table.Columns["MandatoryField"].IsNullable.Should().BeFalse();
        table.Columns["MandatoryWithDesc"].IsNullable.Should().BeFalse();
        table.Columns["MandatoryWithDesc"].Length.Should().Be(100);
        table.Columns["MandatoryWithDesc"].Comment.Should().Be("Required code");

        // Nullable fields
        table.Columns["NullableWithQuestionMark"].IsNullable.Should().BeTrue();

        table.Columns["NullableWithNullComment"].IsNullable.Should().BeTrue();
        table.Columns["NullableWithNullComment"].Comment.Should().BeNull();

        table.Columns["NullableWithNullableComment"].IsNullable.Should().BeTrue();
        table.Columns["NullableWithNullableComment"].Comment.Should().BeNull();

        table.Columns["NullableWithLengthAndNull"].IsNullable.Should().BeTrue();
        table.Columns["NullableWithLengthAndNull"].Length.Should().Be(50);
        table.Columns["NullableWithLengthAndNull"].Comment.Should().BeNull();

        table.Columns["NullableWithLengthNullAndDesc"].IsNullable.Should().BeTrue();
        table.Columns["NullableWithLengthNullAndDesc"].Length.Should().Be(100);
        table.Columns["NullableWithLengthNullAndDesc"].Comment.Should().Be("Optional display name");

        table.Columns["NullableWithPrecScaleAndNull"].IsNullable.Should().BeTrue();
        table.Columns["NullableWithPrecScaleAndNull"].Precision.Should().Be(18);
        table.Columns["NullableWithPrecScaleAndNull"].Scale.Should().Be(2);
        table.Columns["NullableWithPrecScaleAndNull"].Comment.Should().Be("Optional rate");

        table.Columns["NullableWithNullPrefix"].IsNullable.Should().BeTrue();
        table.Columns["NullableWithNullPrefix"].Comment.Should().Be("User bio");
    }
}

