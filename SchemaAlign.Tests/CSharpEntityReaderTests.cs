using SchemaAlign.Models;
using SchemaAlign.Readers.CSharp;

namespace SchemaAlign.Tests;

public class CSharpEntityReaderTests
{
    private readonly CSharpEntityReader _reader = new();

    [Fact]
    public void Read_EmptyOrWhitespace_ReturnsEmptySchema()
    {
        var schema1 = _reader.Read("");
        schema1.Tables.Should().BeEmpty();

        var schema2 = _reader.Read("   \r\n  ");
        schema2.Tables.Should().BeEmpty();
    }

    [Fact]
    public void Read_SimpleClass_ExtractsTableAndColumns()
    {
        var code = """
            namespace MaskedApp.Models;

            public class SampleEntityAlpha
            {
                public int Id { get; set; }
                public string FieldText { get; set; } = string.Empty;
                public decimal FieldDecimal { get; set; }
                public bool FieldFlag { get; set; }
                public DateTime FieldTimestamp { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        schema.Tables.Should().ContainKey("SampleEntityAlpha");
        var table = schema.Tables["SampleEntityAlpha"];
        table.Name.Should().Be("SampleEntityAlpha");
        table.Schema.Should().Be("dbo");
        table.Columns.Should().HaveCount(5);

        table.Columns["Id"].Type.Should().Be(StandardType.Int);
        table.Columns["Id"].IsNullable.Should().BeFalse();
        table.Columns["Id"].IsIdentity.Should().BeTrue();

        table.Columns["FieldText"].Type.Should().Be(StandardType.String);
        table.Columns["FieldText"].IsNullable.Should().BeFalse();

        table.Columns["FieldDecimal"].Type.Should().Be(StandardType.Decimal);
        table.Columns["FieldDecimal"].IsNullable.Should().BeFalse();

        table.Columns["FieldFlag"].Type.Should().Be(StandardType.Boolean);
        table.Columns["FieldFlag"].IsNullable.Should().BeFalse();

        table.Columns["FieldTimestamp"].Type.Should().Be(StandardType.DateTime);
        table.Columns["FieldTimestamp"].IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Read_RecordClass_ExtractsTableAndColumns()
    {
        var code = """
            namespace MaskedApp.Models;

            public record SampleRecordBeta
            {
                public int Id { get; set; }
                public string RecordData { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        schema.Tables.Should().ContainKey("SampleRecordBeta");
        schema.Tables["SampleRecordBeta"].Columns.Should().ContainKey("Id");
        schema.Tables["SampleRecordBeta"].Columns.Should().ContainKey("RecordData");
    }

    [Fact]
    public void Read_TableAttribute_ExtractsCustomNameAndSchema()
    {
        var code = """
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Models;

            [Table("tbl_custom_target", Schema = "custom_schema")]
            public class SampleEntityWithTableAttribute
            {
                public int Id { get; set; }
                public string EntityName { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        schema.Tables.Should().ContainKey("tbl_custom_target");
        var table = schema.Tables["tbl_custom_target"];
        table.Name.Should().Be("tbl_custom_target");
        table.Schema.Should().Be("custom_schema");
    }

    [Fact]
    public void Read_NullableTypes_ExtractsIsNullableCorrectly()
    {
        var code = """
            using System;

            namespace MaskedApp.Models;

            public class SampleNullableContainer
            {
                public int NonNullableInt { get; set; }
                public int? NullableInt { get; set; }
                public Nullable<long> GenericNullableLong { get; set; }
                public string? NullableString { get; set; }
                public DateTime? NullableDate { get; set; }
                public Guid? NullableGuid { get; set; }
                public double? NullableDouble { get; set; }
            }
            """;

        var schema = _reader.Read(code);
        var table = schema.Tables["SampleNullableContainer"];

        table.Columns["NonNullableInt"].IsNullable.Should().BeFalse();
        table.Columns["NullableInt"].IsNullable.Should().BeTrue();
        table.Columns["GenericNullableLong"].IsNullable.Should().BeTrue();
        table.Columns["NullableString"].IsNullable.Should().BeTrue();
        table.Columns["NullableDate"].IsNullable.Should().BeTrue();
        table.Columns["NullableGuid"].IsNullable.Should().BeTrue();
        table.Columns["NullableDouble"].IsNullable.Should().BeTrue();
    }

    [Fact]
    public void Read_KeyAndRequiredAttributes_SetsPrimaryKeyAndNonNullable()
    {
        var code = """
            using System.ComponentModel.DataAnnotations;

            namespace MaskedApp.Models;

            public class SampleKeyedEntity
            {
                [Key]
                public string KeyIdentifier { get; set; }

                [Required]
                public string MandatoryProperty { get; set; }

                public string? OptionalProperty { get; set; }
            }
            """;

        var schema = _reader.Read(code);
        var table = schema.Tables["SampleKeyedEntity"];

        var keyCol = table.Columns["KeyIdentifier"];
        keyCol.IsPrimaryKey.Should().BeTrue();
        keyCol.IsNullable.Should().BeFalse();
        table.PrimaryKeys.Should().ContainSingle().Which.Should().Be("KeyIdentifier");

        var mandatoryCol = table.Columns["MandatoryProperty"];
        mandatoryCol.IsPrimaryKey.Should().BeFalse();
        mandatoryCol.IsNullable.Should().BeFalse();

        var optionalCol = table.Columns["OptionalProperty"];
        optionalCol.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void Read_StringLengthAndMaxLengthAttributes_SetsLength()
    {
        var code = """
            using System.ComponentModel.DataAnnotations;

            namespace MaskedApp.Models;

            public class SampleLengthConstrainedEntity
            {
                [Key]
                public int Id { get; set; }

                [MaxLength(150)]
                public string MaxLengthProperty { get; set; }

                [StringLength(500, MinimumLength = 10)]
                public string StringLengthProperty { get; set; }

                [StringLength(36)]
                public string FixedCodeProperty { get; set; }
            }
            """;

        var schema = _reader.Read(code);
        var table = schema.Tables["SampleLengthConstrainedEntity"];

        table.Columns["MaxLengthProperty"].Length.Should().Be(150);
        table.Columns["StringLengthProperty"].Length.Should().Be(500);
        table.Columns["FixedCodeProperty"].Length.Should().Be(36);
    }

    [Fact]
    public void Read_ColumnAttribute_SetsCustomNameAndTypeName()
    {
        var code = """
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Models;

            public class SampleAnnotatedColumnEntity
            {
                [Column("col_custom_id")]
                public int Id { get; set; }

                [Column("col_custom_amount", TypeName = "decimal(18,4)")]
                public decimal Amount { get; set; }

                [Column(TypeName = "varchar(100)")]
                public string SkuCode { get; set; }

                [Column(TypeName = "varbinary(max)")]
                public byte[]? BinaryPayload { get; set; }
            }
            """;

        var schema = _reader.Read(code);
        var table = schema.Tables["SampleAnnotatedColumnEntity"];

        table.Columns.Should().ContainKey("col_custom_id");
        table.Columns["col_custom_id"].Type.Should().Be(StandardType.Int);

        table.Columns.Should().ContainKey("col_custom_amount");
        var amountCol = table.Columns["col_custom_amount"];
        amountCol.Type.Should().Be(StandardType.Decimal);
        amountCol.Precision.Should().Be(18);
        amountCol.Scale.Should().Be(4);

        table.Columns.Should().ContainKey("SkuCode");
        var skuCol = table.Columns["SkuCode"];
        skuCol.Type.Should().Be(StandardType.String);
        skuCol.Length.Should().Be(100);

        table.Columns.Should().ContainKey("BinaryPayload");
        var payloadCol = table.Columns["BinaryPayload"];
        payloadCol.Type.Should().Be(StandardType.ByteArray);
        payloadCol.Length.Should().Be(-1);
    }

    [Fact]
    public void Read_NotMappedAttribute_IgnoresClassAndProperty()
    {
        var code = """
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Models;

            [NotMapped]
            public class SampleIgnoredEntity
            {
                public int Id { get; set; }
            }

            public class SampleMappedEntity
            {
                public int Id { get; set; }
                public string ValidProperty { get; set; }

                [NotMapped]
                public string TransientProperty { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        schema.Tables.Should().NotContainKey("SampleIgnoredEntity");
        schema.Tables.Should().ContainKey("SampleMappedEntity");
        schema.Tables["SampleMappedEntity"].Columns.Should().ContainKey("Id");
        schema.Tables["SampleMappedEntity"].Columns.Should().ContainKey("ValidProperty");
        schema.Tables["SampleMappedEntity"].Columns.Should().NotContainKey("TransientProperty");
    }

    [Fact]
    public void Read_Inheritance_DerivedClassInheritsBaseProperties()
    {
        var code = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Models;

            public abstract class BaseMaskedAuditEntity
            {
                [Column("AuditCreatedBy")]
                [Required]
                [StringLength(36)]
                public string AuditCreatedBy { get; set; }

                [Column("AuditCreatedAt")]
                [Required]
                public DateTime AuditCreatedAt { get; set; }

                [Column("AuditUpdatedBy")]
                [StringLength(36)]
                public string? AuditUpdatedBy { get; set; }

                [Column("AuditUpdatedAt")]
                public DateTime? AuditUpdatedAt { get; set; }

                [Column("AuditIsActive")]
                [Required]
                public bool AuditIsActive { get; set; }
            }

            [Table("tbl_derived_sample")]
            public class DerivedSampleEntity : BaseMaskedAuditEntity
            {
                [Key]
                [StringLength(36)]
                public string IdEntity { get; set; }

                [StringLength(150)]
                [Required]
                public string EntityTitle { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        // Abstract base class without [Table] should not be its own table
        schema.Tables.Should().NotContainKey("BaseMaskedAuditEntity");

        schema.Tables.Should().ContainKey("tbl_derived_sample");
        var table = schema.Tables["tbl_derived_sample"];

        table.Columns.Should().ContainKey("IdEntity");
        table.Columns["IdEntity"].IsPrimaryKey.Should().BeTrue();
        table.Columns["IdEntity"].Length.Should().Be(36);

        table.Columns.Should().ContainKey("EntityTitle");
        table.Columns["EntityTitle"].Length.Should().Be(150);

        // Inherited audit fields
        table.Columns.Should().ContainKey("AuditCreatedBy");
        table.Columns["AuditCreatedBy"].Length.Should().Be(36);
        table.Columns["AuditCreatedBy"].IsNullable.Should().BeFalse();

        table.Columns.Should().ContainKey("AuditCreatedAt");
        table.Columns["AuditCreatedAt"].Type.Should().Be(StandardType.DateTime);
        table.Columns["AuditCreatedAt"].IsNullable.Should().BeFalse();

        table.Columns.Should().ContainKey("AuditUpdatedBy");
        table.Columns["AuditUpdatedBy"].IsNullable.Should().BeTrue();

        table.Columns.Should().ContainKey("AuditUpdatedAt");
        table.Columns["AuditUpdatedAt"].IsNullable.Should().BeTrue();

        table.Columns.Should().ContainKey("AuditIsActive");
        table.Columns["AuditIsActive"].Type.Should().Be(StandardType.Boolean);
        table.Columns["AuditIsActive"].IsNullable.Should().BeFalse();
    }

    [Fact]
    public void Read_NavigationProperties_MappedToForeignKeysAndExcludedFromColumns()
    {
        var code = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Models;

            [Table("tbl_principal_entity")]
            public class PrincipalEntity
            {
                [Key]
                [StringLength(36)]
                public string IdPrincipal { get; set; }

                [Required]
                [StringLength(100)]
                public string PrincipalName { get; set; }

                public virtual ICollection<DependentEntity> Dependents { get; set; }
            }

            [Table("tbl_dependent_entity")]
            public class DependentEntity
            {
                [Key]
                [StringLength(36)]
                public string IdDependent { get; set; }

                [Required]
                [StringLength(150)]
                public string DependentTitle { get; set; }

                [StringLength(36)]
                [ForeignKey("Principal")]
                [Required]
                public string IdPrincipal { get; set; }

                public virtual PrincipalEntity Principal { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        var principalTable = schema.Tables["tbl_principal_entity"];
        var dependentTable = schema.Tables["tbl_dependent_entity"];

        // Navigation properties should NOT be regular columns
        principalTable.Columns.Should().NotContainKey("Dependents");
        dependentTable.Columns.Should().NotContainKey("Principal");

        // Columns should be present
        dependentTable.Columns.Should().ContainKey("IdDependent");
        dependentTable.Columns.Should().ContainKey("DependentTitle");
        dependentTable.Columns.Should().ContainKey("IdPrincipal");

        // Foreign key should be mapped on tbl_dependent_entity
        dependentTable.ForeignKeys.Should().NotBeEmpty();
        var fk = dependentTable.ForeignKeys.FirstOrDefault(f => f.PrincipalTable == "tbl_principal_entity");
        fk.Should().NotBeNull();
        fk!.DependentTable.Should().Be("tbl_dependent_entity");
        fk.DependentColumn.Should().Be("IdPrincipal");
        fk.PrincipalTable.Should().Be("tbl_principal_entity");
        fk.PrincipalColumn.Should().Be("IdPrincipal");
        fk.Cardinality.Should().Be(ForeignKeyCardinality.ManyToOne);
    }

    [Fact]
    public void Read_ForeignKeyOnNavigationProperty_ResolvesCorrectly()
    {
        var code = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Models;

            public class ParentGroup
            {
                [Key]
                public int GroupIdentifier { get; set; }
                public string GroupName { get; set; }
            }

            public class ChildMember
            {
                [Key]
                public int MemberIdentifier { get; set; }
                public string MemberName { get; set; }

                public int GroupRefId { get; set; }

                [ForeignKey("GroupRefId")]
                public virtual ParentGroup ParentGroup { get; set; }
            }
            """;

        var schema = _reader.Read(code);

        var memberTable = schema.Tables["ChildMember"];
        memberTable.Columns.Should().NotContainKey("ParentGroup");
        memberTable.Columns.Should().ContainKey("GroupRefId");

        var fk = memberTable.ForeignKeys.FirstOrDefault(f => f.PrincipalTable == "ParentGroup");
        fk.Should().NotBeNull();
        fk!.DependentColumn.Should().Be("GroupRefId");
        fk.PrincipalTable.Should().Be("ParentGroup");
        fk.PrincipalColumn.Should().Be("GroupIdentifier");
    }

    [Fact]
    public void Read_XmlDocComments_ExtractsCommentsOnTablesAndColumns()
    {
        var code = """
            namespace MaskedApp.Models;

            /// <summary>
            /// Represents a masked entity for testing.
            /// </summary>
            public class MaskedDocEntity
            {
                /// <summary>
                /// Unique primary key.
                /// </summary>
                public int Id { get; set; }

                /// <summary>
                /// Descriptive label property.
                /// </summary>
                public string Label { get; set; }
            }
            """;

        var schema = _reader.Read(code);
        var table = schema.Tables["MaskedDocEntity"];

        table.Comment.Should().Be("Represents a masked entity for testing.");
        table.Columns["Id"].Comment.Should().Be("Unique primary key.");
        table.Columns["Label"].Comment.Should().Be("Descriptive label property.");
    }

    [Fact]
    public void ReadFiles_MultipleFilesWithCrossFileInheritanceAndReferences_ResolvesCorrectly()
    {
        var baseFile = """
            using System;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            namespace MaskedApp.Base;

            public abstract class BaseAuditMetadata
            {
                [Column("AuditUserId")]
                [Required]
                public string AuditUserId { get; set; }

                [Column("AuditTimestamp")]
                public DateTime AuditTimestamp { get; set; }
            }
            """;

        var masterFile = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using MaskedApp.Base;

            namespace MaskedApp.Model;

            [Table("tbl_master_record")]
            public class MasterRecord : BaseAuditMetadata
            {
                [Key]
                public string IdMaster { get; set; }
                public string MasterName { get; set; }

                public virtual ICollection<DetailRecord> Details { get; set; }
            }
            """;

        var detailFile = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using MaskedApp.Base;

            namespace MaskedApp.Model;

            [Table("tbl_detail_record")]
            public class DetailRecord : BaseAuditMetadata
            {
                [Key]
                public string IdDetail { get; set; }
                public string DetailName { get; set; }

                [ForeignKey("Master")]
                public string IdMaster { get; set; }
                public virtual MasterRecord Master { get; set; }
            }
            """;

        var treeBase = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(baseFile, path: "BaseAuditMetadata.cs");
        var treeMaster = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(masterFile, path: "MasterRecord.cs");
        var treeDetail = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(detailFile, path: "DetailRecord.cs");

        var schema = _reader.ReadSyntaxTrees(new[] { treeBase, treeMaster, treeDetail });

        schema.Tables.Should().ContainKey("tbl_master_record");
        schema.Tables.Should().ContainKey("tbl_detail_record");

        var masterTable = schema.Tables["tbl_master_record"];
        masterTable.Columns.Should().ContainKey("IdMaster");
        masterTable.Columns.Should().ContainKey("AuditUserId");
        masterTable.Columns.Should().ContainKey("AuditTimestamp");

        var detailTable = schema.Tables["tbl_detail_record"];
        detailTable.Columns.Should().ContainKey("IdDetail");
        detailTable.Columns.Should().ContainKey("AuditUserId");
        detailTable.Columns.Should().ContainKey("AuditTimestamp");
        detailTable.Columns.Should().ContainKey("IdMaster");

        detailTable.ForeignKeys.Should().ContainSingle(fk => fk.PrincipalTable == "tbl_master_record" && fk.DependentColumn == "IdMaster");
    }

    [Fact]
    public void ReadDirectory_SyntheticDirectoryScanning_ReadsTablesColumnsAndForeignKeys()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"schema_align_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var baseCode = """
                using System;
                using System.ComponentModel.DataAnnotations;
                using System.ComponentModel.DataAnnotations.Schema;

                namespace MaskedApp.Synthetic;

                public abstract class BaseEntityTemplate
                {
                    [Column("CreatedBy")]
                    [Required]
                    [StringLength(36)]
                    public string CreatedBy { get; set; }

                    [Column("CreatedAt")]
                    [Required]
                    public DateTime CreatedAt { get; set; }

                    [Column("UpdatedBy")]
                    [StringLength(36)]
                    public string? UpdatedBy { get; set; }

                    [Column("UpdatedAt")]
                    public DateTime? UpdatedAt { get; set; }

                    [Column("IsActive")]
                    [Required]
                    public bool IsActive { get; set; }
                }
                """;

            var parentCode = """
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;
                using System.ComponentModel.DataAnnotations.Schema;

                namespace MaskedApp.Synthetic;

                [Table("tbl_synthetic_parent")]
                public class SyntheticParent : BaseEntityTemplate
                {
                    [Key]
                    [StringLength(36)]
                    public string IdParent { get; set; }

                    [Required]
                    [StringLength(100)]
                    public string ParentTitle { get; set; }

                    public virtual ICollection<SyntheticChild> Children { get; set; }
                }
                """;

            var childCode = """
                using System.ComponentModel.DataAnnotations;
                using System.ComponentModel.DataAnnotations.Schema;

                namespace MaskedApp.Synthetic;

                [Table("tbl_synthetic_child")]
                public class SyntheticChild : BaseEntityTemplate
                {
                    [Key]
                    [StringLength(36)]
                    public string IdChild { get; set; }

                    [Required]
                    [StringLength(150)]
                    public string ChildTitle { get; set; }

                    [StringLength(36)]
                    [ForeignKey("Parent")]
                    [Required]
                    public string IdParent { get; set; }

                    public virtual SyntheticParent Parent { get; set; }
                }
                """;

            File.WriteAllText(Path.Combine(tempDir, "BaseEntityTemplate.cs"), baseCode);
            File.WriteAllText(Path.Combine(tempDir, "SyntheticParent.cs"), parentCode);
            File.WriteAllText(Path.Combine(tempDir, "SyntheticChild.cs"), childCode);

            var schema = _reader.ReadDirectory(tempDir);

            schema.Tables.Should().ContainKey("tbl_synthetic_parent");
            schema.Tables.Should().ContainKey("tbl_synthetic_child");

            var parentTable = schema.Tables["tbl_synthetic_parent"];
            parentTable.Columns.Should().ContainKey("IdParent");
            parentTable.Columns.Should().ContainKey("CreatedBy");
            parentTable.Columns.Should().ContainKey("CreatedAt");

            var childTable = schema.Tables["tbl_synthetic_child"];
            childTable.Columns.Should().ContainKey("IdChild");
            childTable.Columns.Should().ContainKey("ChildTitle");
            childTable.Columns.Should().ContainKey("CreatedBy");
            childTable.Columns.Should().ContainKey("CreatedAt");
            childTable.Columns.Should().ContainKey("IdParent");

            childTable.ForeignKeys.Should().Contain(fk => fk.PrincipalTable == "tbl_synthetic_parent" && fk.DependentColumn == "IdParent");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
