using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SchemaAlign.Appliers.CSharp;
using SchemaAlign.Appliers.Diff;
using SchemaAlign.Diff;
using SchemaAlign.Models;
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
            Kind = DiffKind.Modified
        };

        var newCol = new ColumnSchema
        {
            Name = "Email",
            Type = StandardType.String,
            Length = 255,
            IsNullable = true,
            Comment = "User email address"
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Email",
            Kind = DiffKind.Added,
            Target = newCol
        });

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
            Kind = DiffKind.Modified
        };

        var srcId = new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true };
        var tgtId = new ColumnSchema { Name = "Id", Type = StandardType.BigInt, IsPrimaryKey = true };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Id",
            Kind = DiffKind.Modified,
            Source = srcId,
            Target = tgtId,
            Changes = ChangeDetail.TypeChanged
        });

        var srcDesc = new ColumnSchema { Name = "Description", Type = StandardType.String, IsNullable = false };
        var tgtDesc = new ColumnSchema { Name = "Description", Type = StandardType.String, IsNullable = true };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Description",
            Kind = DiffKind.Modified,
            Source = srcDesc,
            Target = tgtDesc,
            Changes = ChangeDetail.NullabilityChanged
        });

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
            Kind = DiffKind.Modified
        };

        var srcId = new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = false, IsIdentity = false };
        var tgtId = new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true, IsIdentity = true };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Id",
            Kind = DiffKind.Modified,
            Source = srcId,
            Target = tgtId,
            Changes = ChangeDetail.KeyStatusChanged | ChangeDetail.IdentityChanged
        });

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
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "emp_code",
            Type = StandardType.String,
            Length = 20,
            IsNullable = false
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "emp_code",
            Kind = DiffKind.Added,
            Target = col
        });

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
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Amount",
            Type = StandardType.Decimal,
            Precision = 18,
            Scale = 2
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Amount",
            Kind = DiffKind.Added,
            Target = col
        });

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
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Value",
            Type = StandardType.String,
            IsNullable = true
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Value",
            Kind = DiffKind.Added,
            Target = col
        });

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Replace("\r\n", "\n").Should().Contain("namespace MyApp.Entities\n{");
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
            var userTableDiff = new TableDiff { TableName = "User", Kind = DiffKind.Modified };
            userTableDiff.Columns.Add(new ColumnDiff
            {
                ColumnName = "Email",
                Kind = DiffKind.Added,
                Target = new ColumnSchema
                {
                    Name = "Email",
                    Type = StandardType.String,
                    Length = 100,
                    IsNullable = true
                }
            });
            diff.Tables.Add(userTableDiff);

            // Added table Role
            var roleTable = new TableSchema { Name = "Roles" };
            roleTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            roleTable.AddColumn(new ColumnSchema { Name = "Name", Type = StandardType.String, Length = 50 });
            diff.Tables.Add(new TableDiff
            {
                TableName = "Roles",
                Kind = DiffKind.Added,
                Target = roleTable
            });

            var options = new CSharpApplierOptions
            {
                TargetDirectory = tempDir,
                DefaultNamespace = "TestApp.Entities"
            };

            // Test PreviewAsync
            var previews = await _applier.PreviewAsync(diff, options);
            previews.Should().HaveCount(2);

            var userPreview = previews.Single(p => p.FilePath.EndsWith("User.cs"));
            userPreview.DiffKind.Should().Be(DiffKind.Modified);
            userPreview.NewContent.Should().Contain("Email");
            userPreview.UnifiedDiff.Should().Contain("+    public string? Email { get; set; }");

            var rolePreview = previews.Single(p => p.FilePath.EndsWith("Role.cs") || p.FilePath.EndsWith("Roles.cs"));
            rolePreview.DiffKind.Should().Be(DiffKind.Added);
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
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Action",
            Type = StandardType.String,
            Length = 100,
            IsNullable = false
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Action",
            Kind = DiffKind.Added,
            Target = col
        });

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
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "NewProp",
            Type = StandardType.Int,
            IsNullable = true
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "NewProp",
            Kind = DiffKind.Added,
            Target = col
        });

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Replace("\r\n", "\n").Should().Contain("public class UnrelatedClass\n{\n    public string UnrelatedProp { get; set; }\n}");
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
            Kind = DiffKind.Unchanged
        };

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Be(originalCode);
    }

    [Fact]
    public void GenerateEntitySource_MultipleForeignKeysSamePrincipalTable_DerivesDistinctNavigationPropertyNames()
    {
        var table = new TableSchema
        {
            Name = "WorkflowRequests"
        };

        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table.AddColumn(new ColumnSchema { Name = "IdRequester", Type = StandardType.Int, IsNullable = false });
        table.AddColumn(new ColumnSchema { Name = "IdApprover", Type = StandardType.Int, IsNullable = true });

        table.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_WorkflowRequests_Requester",
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "WorkflowRequests",
            DependentColumn = "IdRequester",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });

        table.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_WorkflowRequests_Approver",
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "WorkflowRequests",
            DependentColumn = "IdApprover",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });

        var generatedCode = _applier.GenerateEntitySource(table);

        generatedCode.Should().Contain("[ForeignKey(\"IdRequester\")]");
        generatedCode.Should().Contain("public virtual User? Requester { get; set; }");
        generatedCode.Should().Contain("[ForeignKey(\"IdApprover\")]");
        generatedCode.Should().Contain("public virtual User? Approver { get; set; }");
    }

    [Fact]
    public async Task PreviewAndApplyAsync_WithDeleteDroppedTables_GeneratesDeletedDiffAndDeletesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_DeleteTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var oldTableFilePath = Path.Combine(tempDir, "LegacyLog.cs");
            var oldTableContent = """
                namespace TestApp.Entities;

                public class LegacyLog
                {
                    public int Id { get; set; }
                }
                """;
            await File.WriteAllTextAsync(oldTableFilePath, oldTableContent);

            var diff = new SchemaDiff();
            diff.Tables.Add(new TableDiff
            {
                TableName = "LegacyLog",
                Kind = DiffKind.Deleted,
                Source = new TableSchema { Name = "LegacyLog" }
            });

            // Default options: DeleteDroppedTables is false -> no deleted previews
            var defaultOptions = new CSharpApplierOptions { TargetDirectory = tempDir, DeleteDroppedTables = false };
            var defaultPreviews = await _applier.PreviewAsync(diff, defaultOptions);
            defaultPreviews.Should().BeEmpty();

            // Opt-in: DeleteDroppedTables is true -> deleted preview generated
            var deleteOptions = new CSharpApplierOptions { TargetDirectory = tempDir, DeleteDroppedTables = true };
            var deletePreviews = await _applier.PreviewAsync(diff, deleteOptions);
            deletePreviews.Should().HaveCount(1);
            deletePreviews[0].DiffKind.Should().Be(DiffKind.Deleted);
            deletePreviews[0].FilePath.Should().Be(oldTableFilePath);
            deletePreviews[0].UnifiedDiff.Should().Contain("--- a/LegacyLog.cs");
            deletePreviews[0].UnifiedDiff.Should().Contain("+++ /dev/null");

            // Apply with DeleteDroppedTables = true
            var result = await _applier.ApplyAsync(diff, deleteOptions);
            result.Success.Should().BeTrue();
            result.DeletedFiles.Should().Contain(oldTableFilePath);
            File.Exists(oldTableFilePath).Should().BeFalse();
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
    public void ApplyToSource_AddingColumnWithAttributes_ProducesSyntacticallyValidAttributeArgumentListInAST()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class Item
            {
                public int Id { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Item",
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "item_code",
            Type = StandardType.String,
            Length = 50,
            IsNullable = false
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "item_code",
            Kind = DiffKind.Added,
            Target = col
        });

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        var tree = CSharpSyntaxTree.ParseText(updatedCode);
        var root = tree.GetCompilationUnitRoot();
        var prop = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().First(p => p.Identifier.Text == "ItemCode");

        var columnAttr = prop.AttributeLists.SelectMany(al => al.Attributes).FirstOrDefault(a => a.Name.ToString() == "Column");
        columnAttr.Should().NotBeNull();
        columnAttr!.ArgumentList.Should().NotBeNull();
        columnAttr.ArgumentList!.Arguments.Should().HaveCount(1);
        columnAttr.ArgumentList.Arguments[0].Expression.ToString().Should().Be("\"item_code\"");

        var maxLenAttr = prop.AttributeLists.SelectMany(al => al.Attributes).FirstOrDefault(a => a.Name.ToString() == "MaxLength");
        maxLenAttr.Should().NotBeNull();
        maxLenAttr!.ArgumentList.Should().NotBeNull();
        maxLenAttr.ArgumentList!.Arguments[0].Expression.ToString().Should().Be("50");
    }

    [Fact]
    public void ApplyToSource_AddingNonNullableStringColumn_AddsStringEmptyInitializerWhenNrtEnabled()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class Profile
            {
                public int Id { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "Profile",
            Kind = DiffKind.Modified
        };

        var col = new ColumnSchema
        {
            Name = "Username",
            Type = StandardType.String,
            Length = 100,
            IsNullable = false
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Username",
            Kind = DiffKind.Added,
            Target = col
        });

        var options = new CSharpApplierOptions { UseNullableReferenceTypes = true };
        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff, options);

        updatedCode.Should().Contain("public string Username { get; set; } = string.Empty;");

        var optionsNrtDisabled = new CSharpApplierOptions { UseNullableReferenceTypes = false };
        var updatedCodeNrtDisabled = _applier.ApplyToSource(originalCode, tableDiff, optionsNrtDisabled);

        updatedCodeNrtDisabled.Should().Contain("public string Username { get; set; }");
        updatedCodeNrtDisabled.Should().NotContain("= string.Empty;");
    }

    [Fact]
    public void ApplyToSource_ModifyingDecimalColumn_AddsPrecisionAttribute()
    {
        var originalCode = """
            namespace MyApp.Entities;

            public class InvoiceItem
            {
                public int Id { get; set; }
                public decimal Amount { get; set; }
            }
            """;

        var tableDiff = new TableDiff
        {
            TableName = "InvoiceItem",
            Kind = DiffKind.Modified
        };

        var srcCol = new ColumnSchema { Name = "Amount", Type = StandardType.Decimal };
        var tgtCol = new ColumnSchema { Name = "Amount", Type = StandardType.Decimal, Precision = 18, Scale = 4 };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Amount",
            Kind = DiffKind.Modified,
            Source = srcCol,
            Target = tgtCol,
            Changes = ChangeDetail.PrecisionChanged | ChangeDetail.ScaleChanged
        });

        var updatedCode = _applier.ApplyToSource(originalCode, tableDiff);

        updatedCode.Should().Contain("[Precision(18, 4)]");
        updatedCode.Should().Contain("public decimal Amount { get; set; }");
        updatedCode.Should().Contain("using System.ComponentModel.DataAnnotations;");
    }

    [Fact]
    public void GenerateEntitySource_NavigationPropertyCollidingWithColumnName_ResolvesCollisionWithEntitySuffix()
    {
        var table = new TableSchema
        {
            Name = "Assignments"
        };

        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        table.AddColumn(new ColumnSchema { Name = "User", Type = StandardType.String, Length = 50 });
        table.AddColumn(new ColumnSchema { Name = "UserId", Type = StandardType.Int });

        table.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Assignments_User",
            PrincipalTable = "Users",
            PrincipalColumn = "Id",
            DependentTable = "Assignments",
            DependentColumn = "UserId",
            Cardinality = ForeignKeyCardinality.ManyToOne
        });

        var generatedCode = _applier.GenerateEntitySource(table);

        generatedCode.Should().Contain("public string User { get; set; }");
        generatedCode.Should().Contain("public virtual User? UserEntity { get; set; }");
    }

    [Fact]
    public async Task GenerateEvidenceArtifact_EndToEndVerification()
    {
        var evidenceDir = @"C:\Users\william.susanto\.no-mistakes\evidence\01M1DRZY5ET5KPJX0TECYCK067";
        if (!Directory.Exists(evidenceDir))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_Evidence_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# C# Entity Roslyn Applier - End-to-End Verification Evidence");
            sb.AppendLine();
            sb.AppendLine($"**Execution Timestamp:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("**Target Feature:** C# Entity Roslyn Applier (`CSharpEntityApplier`, `CSharpEntityRewriter`, `CSharpEntityGenerator`, `UnifiedDiffGenerator`)");
            sb.AppendLine("**Branch:** `feat/task-6-csharp-applier`");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            // Scenario 1: Existing File Modification (AST Rewriting)
            var userFilePath = Path.Combine(tempDir, "User.cs");
            var userOriginal = """
                // <auto-generated>
                //   Header trivia preserved
                // </auto-generated>

                namespace Acme.Commerce.Entities;

                /// <summary>
                /// Represents an active system user.
                /// </summary>
                public class User
                {
                    public int Id { get; set; }

                    public string Username { get; set; } = string.Empty;

                    /// <summary>
                    /// Custom business logic method that must not be deleted or modified.
                    /// </summary>
                    public bool IsAdmin() => Username.Equals("admin", System.StringComparison.OrdinalIgnoreCase);
                }
                """;
            await File.WriteAllTextAsync(userFilePath, userOriginal);

            // Scenario 2: Legacy File to be deleted
            var legacyFilePath = Path.Combine(tempDir, "LegacyAuditLog.cs");
            var legacyOriginal = """
                namespace Acme.Commerce.Entities;

                public class LegacyAuditLog
                {
                    public int Id { get; set; }
                    public string Details { get; set; }
                }
                """;
            await File.WriteAllTextAsync(legacyFilePath, legacyOriginal);

            // Construct SchemaDiff
            var diff = new SchemaDiff();

            // 1. Added table: OrderItems
            var orderItemTable = new TableSchema
            {
                Name = "OrderItems",
                Schema = "sales",
                Comment = "Line items associated with customer purchase orders."
            };
            orderItemTable.AddColumn(new ColumnSchema
            {
                Name = "Id",
                Type = StandardType.BigInt,
                IsPrimaryKey = true,
                IsIdentity = true,
                Comment = "Primary key"
            });
            orderItemTable.AddColumn(new ColumnSchema
            {
                Name = "OrderId",
                Type = StandardType.BigInt,
                IsNullable = false
            });
            orderItemTable.AddColumn(new ColumnSchema
            {
                Name = "Sku",
                Type = StandardType.String,
                Length = 50,
                IsNullable = false
            });
            orderItemTable.AddColumn(new ColumnSchema
            {
                Name = "UnitPrice",
                Type = StandardType.Decimal,
                Precision = 18,
                Scale = 2,
                IsNullable = false
            });
            orderItemTable.AddColumn(new ColumnSchema
            {
                Name = "Quantity",
                Type = StandardType.Int,
                IsNullable = false
            });
            orderItemTable.AddForeignKey(new ForeignKeySchema
            {
                ConstraintName = "FK_OrderItems_Orders",
                PrincipalTable = "Orders",
                PrincipalColumn = "Id",
                DependentTable = "OrderItems",
                DependentColumn = "OrderId",
                Cardinality = ForeignKeyCardinality.ManyToOne
            });
            diff.Tables.Add(new TableDiff
            {
                TableName = "OrderItems",
                Schema = "sales",
                Kind = DiffKind.Added,
                Target = orderItemTable
            });

            // 2. Added table: WorkflowRequests (Multiple FKs same target table)
            var workflowTable = new TableSchema { Name = "WorkflowRequests" };
            workflowTable.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            workflowTable.AddColumn(new ColumnSchema { Name = "IdRequester", Type = StandardType.Int });
            workflowTable.AddColumn(new ColumnSchema { Name = "IdApprover", Type = StandardType.Int, IsNullable = true });
            workflowTable.AddColumn(new ColumnSchema { Name = "CreatedByUserId", Type = StandardType.Int });
            workflowTable.AddForeignKey(new ForeignKeySchema
            {
                ConstraintName = "FK_WF_Requester",
                PrincipalTable = "Users",
                PrincipalColumn = "Id",
                DependentTable = "WorkflowRequests",
                DependentColumn = "IdRequester",
                Cardinality = ForeignKeyCardinality.ManyToOne
            });
            workflowTable.AddForeignKey(new ForeignKeySchema
            {
                ConstraintName = "FK_WF_Approver",
                PrincipalTable = "Users",
                PrincipalColumn = "Id",
                DependentTable = "WorkflowRequests",
                DependentColumn = "IdApprover",
                Cardinality = ForeignKeyCardinality.ManyToOne
            });
            workflowTable.AddForeignKey(new ForeignKeySchema
            {
                ConstraintName = "FK_WF_CreatedBy",
                PrincipalTable = "Users",
                PrincipalColumn = "Id",
                DependentTable = "WorkflowRequests",
                DependentColumn = "CreatedByUserId",
                Cardinality = ForeignKeyCardinality.ManyToOne
            });
            diff.Tables.Add(new TableDiff
            {
                TableName = "WorkflowRequests",
                Kind = DiffKind.Added,
                Target = workflowTable
            });

            // 3. Modified table: User
            var userTableDiff = new TableDiff
            {
                TableName = "User",
                Kind = DiffKind.Modified
            };
            // Modified Id: int -> long with Key & Identity
            userTableDiff.Columns.Add(new ColumnDiff
            {
                ColumnName = "Id",
                Kind = DiffKind.Modified,
                Source = new ColumnSchema { Name = "Id", Type = StandardType.Int },
                Target = new ColumnSchema { Name = "Id", Type = StandardType.BigInt, IsPrimaryKey = true, IsIdentity = true },
                Changes = ChangeDetail.TypeChanged | ChangeDetail.KeyStatusChanged | ChangeDetail.IdentityChanged
            });
            // Added Email (nullable string with max length)
            userTableDiff.Columns.Add(new ColumnDiff
            {
                ColumnName = "Email",
                Kind = DiffKind.Added,
                Target = new ColumnSchema { Name = "Email", Type = StandardType.String, Length = 255, IsNullable = true, Comment = "Primary contact email" }
            });
            // Added phone_number (custom db name with Column attribute)
            userTableDiff.Columns.Add(new ColumnDiff
            {
                ColumnName = "phone_number",
                Kind = DiffKind.Added,
                Target = new ColumnSchema { Name = "phone_number", Type = StandardType.String, Length = 20, IsNullable = false }
            });
            diff.Tables.Add(userTableDiff);

            // 4. Deleted table: LegacyAuditLog
            diff.Tables.Add(new TableDiff
            {
                TableName = "LegacyAuditLog",
                Kind = DiffKind.Deleted,
                Source = new TableSchema { Name = "LegacyAuditLog" }
            });

            var options = new CSharpApplierOptions
            {
                TargetDirectory = tempDir,
                DefaultNamespace = "Acme.Commerce.Entities",
                UseFileScopedNamespaces = true,
                UseNullableReferenceTypes = true,
                UseDataAnnotations = true,
                AutoAddMissingUsings = true,
                DeleteDroppedTables = true
            };

            // PREVIEW PHASE
            var previews = await _applier.PreviewAsync(diff, options);

            sb.AppendLine("## 1. Schema Diff Preview (`PreviewAsync`)");
            sb.AppendLine();
            sb.AppendLine($"Generated **{previews.Count}** preview items across target directory `{tempDir}`.");
            sb.AppendLine();

            foreach (var preview in previews)
            {
                var relPath = Path.GetFileName(preview.FilePath);
                sb.AppendLine($"### File: `{relPath}` (DiffKind: `{preview.DiffKind}`)");
                sb.AppendLine();
                sb.AppendLine("```diff");
                sb.AppendLine(preview.UnifiedDiff.TrimEnd());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // APPLY PHASE
            var applyResult = await _applier.ApplyAsync(diff, options);

            sb.AppendLine("## 2. Schema Diff Application (`ApplyAsync`)");
            sb.AppendLine();
            sb.AppendLine($"- **Success**: `{applyResult.Success}`");
            sb.AppendLine($"- **Created Files ({applyResult.CreatedFiles.Count})**:");
            foreach (var file in applyResult.CreatedFiles) sb.AppendLine($"  - `{Path.GetFileName(file)}`");
            sb.AppendLine($"- **Changed Files ({applyResult.ChangedFiles.Count})**:");
            foreach (var file in applyResult.ChangedFiles) sb.AppendLine($"  - `{Path.GetFileName(file)}`");
            sb.AppendLine($"- **Deleted Files ({applyResult.DeletedFiles.Count})**:");
            foreach (var file in applyResult.DeletedFiles) sb.AppendLine($"  - `{Path.GetFileName(file)}`");
            sb.AppendLine();

            sb.AppendLine("## 3. Resulting File Contents On Disk");
            sb.AppendLine();

            // OrderItem.cs
            var orderItemPath = Path.Combine(tempDir, "OrderItem.cs");
            if (File.Exists(orderItemPath))
            {
                sb.AppendLine("### Generated `OrderItem.cs` (New Table Entity):");
                sb.AppendLine("```csharp");
                sb.AppendLine(await File.ReadAllTextAsync(orderItemPath));
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // WorkflowRequest.cs
            var wfPath = Path.Combine(tempDir, "WorkflowRequest.cs");
            if (File.Exists(wfPath))
            {
                sb.AppendLine("### Generated `WorkflowRequest.cs` (Disambiguated Foreign Keys):");
                sb.AppendLine("```csharp");
                sb.AppendLine(await File.ReadAllTextAsync(wfPath));
                sb.AppendLine("```");
                sb.AppendLine();
            }

            // User.cs
            if (File.Exists(userFilePath))
            {
                sb.AppendLine("### Modified `User.cs` (Roslyn AST In-place Rewrite):");
                sb.AppendLine("```csharp");
                sb.AppendLine(await File.ReadAllTextAsync(userFilePath));
                sb.AppendLine("```");
                sb.AppendLine();
            }

            sb.AppendLine("## 4. Verification Assertions");
            sb.AppendLine();
            sb.AppendLine("- [x] `OrderItem.cs` contains `[Table(\"OrderItems\", Schema = \"sales\")]` and XML comments.");
            sb.AppendLine("- [x] `OrderItem.cs` initializes non-nullable string `Sku` with `= string.Empty;`.");
            sb.AppendLine("- [x] `OrderItem.cs` generates navigation property `public virtual Order? Order { get; set; }` with `[ForeignKey(\"OrderId\")]`.");
            sb.AppendLine("- [x] `WorkflowRequest.cs` resolves multiple FKs to `Users` into `Requester`, `Approver`, and `CreatedByUser`.");
            sb.AppendLine("- [x] `User.cs` AST rewrite preserves header comments, existing `IsAdmin()` method, and updates `Id` to `long` with `[Key]` & `[DatabaseGenerated]`.");
            sb.AppendLine("- [x] `User.cs` automatically imports `System.ComponentModel.DataAnnotations` and `System.ComponentModel.DataAnnotations.Schema`.");
            sb.AppendLine("- [x] `LegacyAuditLog.cs` is deleted from disk when `DeleteDroppedTables` is true.");
            sb.AppendLine();

            var evidencePath = Path.Combine(evidenceDir, "csharp-applier-end-to-end-evidence.md");
            await File.WriteAllTextAsync(evidencePath, sb.ToString());

            // Assertions
            applyResult.Success.Should().BeTrue();
            applyResult.CreatedFiles.Should().HaveCount(2);
            applyResult.ChangedFiles.Should().HaveCount(1);
            applyResult.DeletedFiles.Should().HaveCount(1);

            File.Exists(legacyFilePath).Should().BeFalse();
            File.Exists(orderItemPath).Should().BeTrue();
            File.Exists(userFilePath).Should().BeTrue();

            var finalUserCode = await File.ReadAllTextAsync(userFilePath);
            finalUserCode.Should().Contain("public long Id { get; set; }");
            finalUserCode.Should().Contain("[Key]");
            finalUserCode.Should().Contain("[Column(\"phone_number\")]");
            finalUserCode.Should().Contain("public string PhoneNumber { get; set; } = string.Empty;");
            finalUserCode.Should().Contain("public bool IsAdmin()");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}



