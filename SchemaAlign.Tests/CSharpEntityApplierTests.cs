using FluentAssertions;
using SchemaAlign.Appliers.CSharp;
using SchemaAlign.Appliers.Diff;
using SchemaAlign.Models;
using SchemaAlign.Models.Diff;
using Xunit;

namespace SchemaAlign.Tests;

public class CSharpEntityApplierTests
{
    private readonly CSharpEntityApplier _applier = new();

    [Fact]
    public void ApplyToSource_AddingNewColumn_AppendsPropertyPreservingFormattingAndExistingMembers()
    {
        var originalCode = """
            using System;

            namespace MyApp.Entities;

            /// <summary>
            /// User entity
            /// </summary>
            public class User
            {
                public int Id { get; set; }

                public string Name { get; set; } = string.Empty;

                public void CustomMethod()
                {
                    // Existing logic
                }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "User",
            DiffType = DiffType.Modified
        };

        var newCol = new ColumnSchema
        {
            Name = "Email",
            Type = StandardType.String,
            Length = 255,
            IsNullable = true,
            Comment = "User email address"
        };
        tableDiff.ColumnDiffs["Email"] = ColumnDiff.Added(newCol);

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("public string? Email { get; set; }");
        updatedCode.Should().Contain("[MaxLength(255)]");
        updatedCode.Should().Contain("/// User email address");
        updatedCode.Should().Contain("public void CustomMethod()");
        updatedCode.Should().Contain("// Existing logic");
        updatedCode.Should().Contain("using System.ComponentModel.DataAnnotations;");
    }

    [Fact]
    public void ApplyToSource_ModifyingColumnTypeAndNullability_UpdatesPropertyPreservingTrivia()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class Product
            {
                /// <summary>
                /// Unique product ID
                /// </summary>
                public int Id { get; set; }

                // Product description
                public string Description { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Product",
            DiffType = DiffType.Modified
        };

        var srcId = new ColumnSchema { Name = "Id", Type = StandardType.BigInt, IsPrimaryKey = true };
        var tgtId = new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true };
        tableDiff.ColumnDiffs["Id"] = ColumnDiff.Compare(srcId, tgtId)!;

        var srcDesc = new ColumnSchema { Name = "Description", Type = StandardType.String, IsNullable = true };
        var tgtDesc = new ColumnSchema { Name = "Description", Type = StandardType.String, IsNullable = false };
        tableDiff.ColumnDiffs["Description"] = ColumnDiff.Compare(srcDesc, tgtDesc)!;

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("public long Id { get; set; }");
        updatedCode.Should().Contain("/// Unique product ID");
        updatedCode.Should().Contain("public string? Description { get; set; }");
        updatedCode.Should().Contain("// Product description");
    }

    [Fact]
    public void ApplyToSource_AddingKeyAndDatabaseGeneratedAttributes_InsertsAttributes()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class Customer
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Customer",
            DiffType = DiffType.Modified
        };

        var srcId = new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true, IsIdentity = true };
        var tgtId = new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = false, IsIdentity = false };
        tableDiff.ColumnDiffs["Id"] = ColumnDiff.Compare(srcId, tgtId)!;

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("[Key]");
        updatedCode.Should().Contain("[DatabaseGenerated(DatabaseGeneratedOption.Identity)]");
        updatedCode.Should().Contain("using System.ComponentModel.DataAnnotations;");
        updatedCode.Should().Contain("using System.ComponentModel.DataAnnotations.Schema;");
    }

    [Fact]
    public void ApplyToSource_AddingColumnWithCustomDbName_InsertsColumnAttribute()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class Employee
            {
                public int Id { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Employee",
            DiffType = DiffType.Modified
        };

        var col = new ColumnSchema
        {
            Name = "emp_code",
            Type = StandardType.String,
            Length = 20,
            IsNullable = false
        };
        tableDiff.ColumnDiffs["emp_code"] = ColumnDiff.Added(col);

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("[Column(\"emp_code\")]");
        updatedCode.Should().Contain("[MaxLength(20)]");
        updatedCode.Should().Contain("public string EmpCode { get; set; }");
    }

    [Fact]
    public void ApplyToSource_PreservesHeaderCommentsWhenAddingUsings()
    {
        var originalCode = """
            // <auto-generated>
            //   Copyright (c) MyCompany. All rights reserved.
            // </auto-generated>

            using System;

            namespace MyApp.Entities;

            public class Invoice
            {
                public int Id { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Invoice",
            DiffType = DiffType.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Amount",
            Type = StandardType.Decimal,
            Precision = 18,
            Scale = 2
        };
        tableDiff.ColumnDiffs["Amount"] = ColumnDiff.Added(col);

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().StartWith("// <auto-generated>");
        updatedCode.Should().Contain("using System.ComponentModel.DataAnnotations;");
        updatedCode.Should().Contain("public decimal Amount { get; set; }");
    }

    [Fact]
    public void ApplyToSource_BlockScopedNamespace_PreservesBlockStructure()
    {
        var originalCode = """
            namespace MyApp.Entities
            {
                public class Setting
                {
                    public string Key { get; set; }
                }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Setting",
            DiffType = DiffType.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Value",
            Type = StandardType.String,
            IsNullable = true
        };
        tableDiff.ColumnDiffs["Value"] = ColumnDiff.Added(col);

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("namespace MyApp.Entities\n{");
        updatedCode.Should().Contain("public string? Value { get; set; }");
    }

    [Fact]
    public void GenerateEntitySource_BrandNewTable_GeneratesCleanEntityWithAllAttributes()
    {
        var table = new TableSchema
        {
            Name = "OrderItems",
            Schema = "sales",
            Comment = "Stores individual items in an order"
        };

        table.AddColumn(new ColumnSchema
        {
            Name = "Id",
            Type = StandardType.BigInt,
            IsPrimaryKey = true,
            IsIdentity = true,
            Comment = "Primary key"
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "OrderId",
            Type = StandardType.BigInt,
            IsNullable = false
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "Sku",
            Type = StandardType.String,
            Length = 50,
            IsNullable = false
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "UnitPrice",
            Type = StandardType.Decimal,
            Precision = 18,
            Scale = 2,
            IsNullable = false
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "Quantity",
            Type = StandardType.Int,
            IsNullable = false
        });

        table.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_OrderItems_Orders",
            PrincipalTable = "Orders",
            PrincipalColumn = "Id",
            DependentTable = "OrderItems",
            DependentColumn = "OrderId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });

        var options = new CSharpApplierOptions
        {
            DefaultNamespace = "Commerce.Data.Entities",
            UseFileScopedNamespaces = true,
            AddSchemaToTableAttribute = true
        };

        var generatedCode = _applier.GenerateEntitySource(table, options);

        generatedCode.Should().Contain("namespace Commerce.Data.Entities;");
        generatedCode.Should().Contain("[Table(\"OrderItems\", Schema = \"sales\")]");
        generatedCode.Should().Contain("/// Stores individual items in an order");
        generatedCode.Should().Contain("public class OrderItem");
        generatedCode.Should().Contain("[Key]");
        generatedCode.Should().Contain("[DatabaseGenerated(DatabaseGeneratedOption.Identity)]");
        generatedCode.Should().Contain("public long Id { get; set; }");
        generatedCode.Should().Contain("[MaxLength(50)]");
        generatedCode.Should().Contain("public string Sku { get; set; } = string.Empty;");
        generatedCode.Should().Contain("public decimal UnitPrice { get; set; }");
        generatedCode.Should().Contain("public int Quantity { get; set; }");
        generatedCode.Should().Contain("[ForeignKey(\"OrderId\")]");
        generatedCode.Should().Contain("public virtual Order? Order { get; set; }");
    }

    [Fact]
    public void GenerateUnifiedDiff_ModifiedFile_ProducesAccurateUnifiedDiff()
    {
        var original = "line1\nline2\nline3\n";
        var modified = "line1\nline2_modified\nline3\nline4\n";

        var diff = UnifiedDiffGenerator.GenerateDiff(original, modified, "Entities/Test.cs");

        diff.Should().Contain("--- a/Entities/Test.cs");
        diff.Should().Contain("+++ b/Entities/Test.cs");
        diff.Should().Contain("@@ -1,3 +1,4 @@");
        diff.Should().Contain("-line2");
        diff.Should().Contain("+line2_modified");
        diff.Should().Contain("+line4");
    }

    [Fact]
    public void GenerateUnifiedDiff_NewFile_ProducesAccurateDiffAgainstDevNull()
    {
        var modified = "public class NewEntity {}\n";

        var diff = UnifiedDiffGenerator.GenerateDiff(null, modified, "Entities/NewEntity.cs");

        diff.Should().Contain("--- /dev/null");
        diff.Should().Contain("+++ b/Entities/NewEntity.cs");
        diff.Should().Contain("+public class NewEntity {}");
    }

    [Fact]
    public async Task PreviewAndApplyAsync_EndToEnd_WorksOnFileSystem()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var userFilePath = Path.Combine(tempDir, "User.cs");
            var userOriginal = """
                namespace TestApp.Entities;

                public class User
                {
                    public int Id { get; set; }
                }
                """;
            await File.WriteAllTextAsync(userFilePath, userOriginal);

            var diff = new SchemaDiff();

            // Modified table User: add Email
            var userTableDiff = new TableDiff { TableName = "User", DiffType = DiffType.Modified };
            userTableDiff.ColumnDiffs["Email"] = ColumnDiff.Added(new ColumnSchema
            {
                Name = "Email",
                Type = StandardType.String,
                Length = 100,
                IsNullable = true
            });
            diff.AddTableDiff(userTableDiff);

            // Added table Role
            var roleTable = new TableSchema { Name = "Roles" };
            roleTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            roleTable.AddColumn(new ColumnSchema { Name = "Name", Type = StandardType.String, Length = 50 });
            diff.AddTableDiff(TableDiff.Added(roleTable));

            var options = new CSharpApplierOptions
            {
                TargetDirectory = tempDir,
                DefaultNamespace = "TestApp.Entities"
            };

            // Test PreviewAsync
            var previews = await _applier.PreviewAsync(diff, options);
            previews.Should().HaveCount(2);

            var userPreview = previews.Single(p => p.FilePath.EndsWith("User.cs"));
            userPreview.DiffType.Should().Be(DiffType.Modified);
            userPreview.NewContent.Should().Contain("Email");
            userPreview.UnifiedDiff.Should().Contain("+    public string? Email { get; set; }");

            var rolePreview = previews.Single(p => p.FilePath.EndsWith("Role.cs") || p.FilePath.EndsWith("Roles.cs"));
            rolePreview.DiffType.Should().Be(DiffType.Added);
            rolePreview.UnifiedDiff.Should().Contain("--- /dev/null");

            // Verify files on disk before apply (User.cs should NOT have Email yet)
            var diskUserBefore = await File.ReadAllTextAsync(userFilePath);
            diskUserBefore.Should().NotContain("Email");

            // Test ApplyAsync
            var result = await _applier.ApplyAsync(diff, options);
            result.Success.Should().BeTrue();
            result.ChangedFiles.Should().HaveCount(1);
            result.CreatedFiles.Should().HaveCount(1);

            // Verify disk files after apply
            var diskUserAfter = await File.ReadAllTextAsync(userFilePath);
            diskUserAfter.Should().Contain("Email");

            var roleFilePath = result.CreatedFiles[0];
            File.Exists(roleFilePath).Should().BeTrue();
            var diskRole = await File.ReadAllTextAsync(roleFilePath);
            diskRole.Should().Contain("public class Role");
            diskRole.Should().Contain("public int Id { get; set; }");
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
    public void ApplyToSource_ClassWithBaseClassAndInterfaces_PreservesInheritance()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class AuditLog : BaseEntity, IAuditable, IDisposable
            {
                public int Id { get; set; }

                public void Dispose() { }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "AuditLog",
            DiffType = DiffType.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Action",
            Type = StandardType.String,
            Length = 100,
            IsNullable = false
        };
        tableDiff.ColumnDiffs["Action"] = ColumnDiff.Added(col);

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("public class AuditLog : BaseEntity, IAuditable, IDisposable");
        updatedCode.Should().Contain("public void Dispose() { }");
        updatedCode.Should().Contain("public string Action { get; set; }");
    }

    [Fact]
    public void ApplyToSource_MultipleClassesInSameFile_OnlyModifiesTargetClass()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class UnrelatedClass
            {
                public string UnrelatedProp { get; set; }
            }

            public class TargetClass
            {
                public int Id { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "TargetClass",
            DiffType = DiffType.Modified
        };

        var col = new ColumnSchema
        {
            Name = "NewProp",
            Type = StandardType.Int,
            IsNullable = true
        };
        tableDiff.ColumnDiffs["NewProp"] = ColumnDiff.Added(col);

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("public class UnrelatedClass\n{\n    public string UnrelatedProp { get; set; }\n}");
        updatedCode.Should().Contain("public int? NewProp { get; set; }");
    }

    [Fact]
    public void ApplyToSource_WhenNoChanges_ReturnsOriginalSourceUnmodified()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class Account
            {
                public int Id { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Account",
            DiffType = DiffType.None
        };

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Be(originalCode);
    }
}
