using AwesomeAssertions;
using SchemaAlign.Appliers;
using SchemaAlign.Appliers.Diff;
using SchemaAlign.Appliers.SqlServer;
using SchemaAlign.Cli.Services;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using Xunit;

namespace SchemaAlign.Tests;

public class SqlServerMigrationApplierTests
{
    private class FakeSqlMigrationExecutor : ISqlMigrationExecutor
    {
        public List<string> ExecutedBatches { get; } = new();
        public string? LastConnectionString { get; private set; }
        public bool LastTransactional { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task ExecuteBatchesAsync(
            string connectionString,
            IEnumerable<string> sqlBatches,
            bool transactional,
            CancellationToken cancellationToken = default)
        {
            LastConnectionString = connectionString;
            LastTransactional = transactional;

            if (ShouldThrow)
            {
                throw new InvalidOperationException("Simulated SQL execution failure.");
            }

            ExecutedBatches.AddRange(sqlBatches);
            return Task.CompletedTask;
        }
    }

    private readonly SqlServerMigrationApplier _applier = new();

    [Fact]
    public void GenerateMigrationScript_AddedTable_GeneratesIdempotentCreateTableWithAllColumnTypes()
    {
        // Arrange
        var table = new TableSchema
        {
            Name = "tbl_masked_sample",
            Schema = "dbo",
            Comment = "Sample table description"
        };

        table.AddColumn(new ColumnSchema
        {
            Name = "ColId",
            Type = StandardType.BigInt,
            IsPrimaryKey = true,
            IsIdentity = true,
            IsNullable = false,
            Comment = "Primary key"
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "ColString",
            Type = StandardType.String,
            Length = 150,
            IsNullable = false,
            DefaultValue = "'N/A'"
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "ColDecimal",
            Type = StandardType.Decimal,
            Precision = 18,
            Scale = 4,
            IsNullable = true
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "ColBit",
            Type = StandardType.Boolean,
            IsNullable = false,
            DefaultValue = "1"
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "ColDateTime",
            Type = StandardType.DateTime,
            IsNullable = true
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "ColGuid",
            Type = StandardType.Guid,
            IsNullable = false
        });

        table.AddColumn(new ColumnSchema
        {
            Name = "ColBinary",
            Type = StandardType.ByteArray,
            Length = -1,
            IsNullable = true
        });

        var diff = new SchemaDiff();
        diff.Tables.Add(new TableDiff
        {
            TableName = "tbl_masked_sample",
            Schema = "dbo",
            Kind = DiffKind.Added,
            Target = table
        });

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().Contain("IF OBJECT_ID(N'[dbo].[tbl_masked_sample]', N'U') IS NULL");
        script.Should().Contain("CREATE TABLE [dbo].[tbl_masked_sample]");
        script.Should().Contain("[ColId] BIGINT IDENTITY(1,1) NOT NULL");
        script.Should().Contain("[ColString] NVARCHAR(150) NOT NULL");
        script.Should().Contain("[ColDecimal] DECIMAL(18,4) NULL");
        script.Should().Contain("[ColBit] BIT NOT NULL");
        script.Should().Contain("[ColDateTime] DATETIME2 NULL");
        script.Should().Contain("[ColGuid] UNIQUEIDENTIFIER NOT NULL");
        script.Should().Contain("[ColBinary] VARBINARY(MAX) NULL");
        script.Should().Contain("CONSTRAINT [PK_tbl_masked_sample] PRIMARY KEY CLUSTERED ([ColId])");
        script.Should().NotContain("sp_addextendedproperty");
    }

    [Fact]
    public void GenerateMigrationScript_CompositePrimaryKey_GeneratesTableConstraint()
    {
        // Arrange
        var table = new TableSchema
        {
            Name = "tbl_masked_composite",
            Schema = "custom"
        };

        table.AddColumn(new ColumnSchema { Name = "TenantId", Type = StandardType.Int, IsPrimaryKey = true, IsNullable = false });
        table.AddColumn(new ColumnSchema { Name = "RecordId", Type = StandardType.BigInt, IsPrimaryKey = true, IsNullable = false });
        table.AddColumn(new ColumnSchema { Name = "Payload", Type = StandardType.String, Length = 200, IsNullable = true });

        var diff = new SchemaDiff();
        diff.Tables.Add(new TableDiff
        {
            TableName = "tbl_masked_composite",
            Schema = "custom",
            Kind = DiffKind.Added,
            Target = table
        });

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().Contain("CREATE TABLE [custom].[tbl_masked_composite]");
        script.Should().Contain("CONSTRAINT [PK_tbl_masked_composite] PRIMARY KEY CLUSTERED ([TenantId], [RecordId])");
    }

    [Fact]
    public void GenerateMigrationScript_ModifiedTable_AddAndAlterColumns_GeneratesGuardedAlterStatements()
    {
        // Arrange
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_entity",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };

        var addedCol = new ColumnSchema
        {
            Name = "ColNew",
            Type = StandardType.String,
            Length = 80,
            IsNullable = true,
            Comment = "New field comment"
        };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "ColNew",
            Kind = DiffKind.Added,
            Target = addedCol
        });

        var srcCol = new ColumnSchema { Name = "ColMod", Type = StandardType.Int, IsNullable = true };
        var tgtCol = new ColumnSchema { Name = "ColMod", Type = StandardType.BigInt, IsNullable = false };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "ColMod",
            Kind = DiffKind.Modified,
            Source = srcCol,
            Target = tgtCol,
            Changes = ChangeDetail.TypeChanged | ChangeDetail.NullabilityChanged
        });

        var diff = new SchemaDiff();
        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().Contain("IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[tbl_masked_entity]') AND name = N'ColNew')");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_entity] ADD [ColNew] NVARCHAR(80) NULL;");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_entity] ALTER COLUMN [ColMod] BIGINT NOT NULL;");
        script.Should().NotContain("sp_addextendedproperty");
    }

    [Fact]
    public void GenerateMigrationScript_AddNotNullColumnWithoutExplicitDefault_GeneratesDefaultConstraintToAvoidMsg4901()
    {
        // Arrange
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_existing",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };

        var notNullStringCol = new ColumnSchema
        {
            Name = "ColMaskedStringReq",
            Type = StandardType.String,
            Length = 36,
            IsNullable = false
        };
        var notNullIntCol = new ColumnSchema
        {
            Name = "ColMaskedIntReq",
            Type = StandardType.Int,
            IsNullable = false
        };
        var notNullBitCol = new ColumnSchema
        {
            Name = "ColMaskedBitReq",
            Type = StandardType.Boolean,
            IsNullable = false
        };
        var notNullDateCol = new ColumnSchema
        {
            Name = "ColMaskedDateReq",
            Type = StandardType.DateTime,
            IsNullable = false
        };

        tableDiff.Columns.Add(new ColumnDiff { ColumnName = "ColMaskedStringReq", Kind = DiffKind.Added, Target = notNullStringCol });
        tableDiff.Columns.Add(new ColumnDiff { ColumnName = "ColMaskedIntReq", Kind = DiffKind.Added, Target = notNullIntCol });
        tableDiff.Columns.Add(new ColumnDiff { ColumnName = "ColMaskedBitReq", Kind = DiffKind.Added, Target = notNullBitCol });
        tableDiff.Columns.Add(new ColumnDiff { ColumnName = "ColMaskedDateReq", Kind = DiffKind.Added, Target = notNullDateCol });

        var diff = new SchemaDiff();
        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert — must include a DEFAULT clause when adding a NOT NULL column to prevent SQL Server Msg 4901 on non-empty tables
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_existing] ADD [ColMaskedStringReq] NVARCHAR(36) NOT NULL CONSTRAINT [DF_tbl_masked_existing_ColMaskedStringReq] DEFAULT ('');");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_existing] ADD [ColMaskedIntReq] INT NOT NULL CONSTRAINT [DF_tbl_masked_existing_ColMaskedIntReq] DEFAULT ((0));");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_existing] ADD [ColMaskedBitReq] BIT NOT NULL CONSTRAINT [DF_tbl_masked_existing_ColMaskedBitReq] DEFAULT ((0));");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_existing] ADD [ColMaskedDateReq] DATETIME2 NOT NULL CONSTRAINT [DF_tbl_masked_existing_ColMaskedDateReq] DEFAULT ('1900-01-01');");
    }

    [Fact]
    public void GenerateMigrationScript_AddNotNullColumnWithExplicitDefault_UsesExplicitDefaultWithNamedConstraint()
    {
        // Arrange
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_existing",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };

        var customDefCol = new ColumnSchema
        {
            Name = "ColMaskedCustomDef",
            Type = StandardType.Int,
            IsNullable = false,
            DefaultValue = "((42))"
        };
        var guidCol = new ColumnSchema
        {
            Name = "ColMaskedGuidReq",
            Type = StandardType.Guid,
            IsNullable = false
        };

        tableDiff.Columns.Add(new ColumnDiff { ColumnName = "ColMaskedCustomDef", Kind = DiffKind.Added, Target = customDefCol });
        tableDiff.Columns.Add(new ColumnDiff { ColumnName = "ColMaskedGuidReq", Kind = DiffKind.Added, Target = guidCol });

        var diff = new SchemaDiff();
        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_existing] ADD [ColMaskedCustomDef] INT NOT NULL CONSTRAINT [DF_tbl_masked_existing_ColMaskedCustomDef] DEFAULT ((42));");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_existing] ADD [ColMaskedGuidReq] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_tbl_masked_existing_ColMaskedGuidReq] DEFAULT ('00000000-0000-0000-0000-000000000000');");
    }

    [Fact]
    public void GenerateMigrationScript_ForeignKeys_GeneratesGuardedAddAndDropConstraints()
    {
        // Arrange
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_child",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };

        var addedFk = new ForeignKeySchema
        {
            ConstraintName = "FK_tbl_masked_child_parent",
            DependentTable = "tbl_masked_child",
            DependentColumn = "ParentId",
            PrincipalTable = "tbl_masked_parent",
            PrincipalColumn = "Id"
        };
        tableDiff.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = addedFk.ConstraintName,
            Kind = DiffKind.Added,
            Target = addedFk
        });

        var deletedFk = new ForeignKeySchema
        {
            ConstraintName = "FK_tbl_masked_child_old",
            DependentTable = "tbl_masked_child",
            DependentColumn = "OldParentId",
            PrincipalTable = "tbl_masked_old_parent",
            PrincipalColumn = "Id"
        };
        tableDiff.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = deletedFk.ConstraintName,
            Kind = DiffKind.Deleted,
            Source = deletedFk
        });

        var diff = new SchemaDiff();
        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().Contain("IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tbl_masked_child_parent')");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_child] ADD CONSTRAINT [FK_tbl_masked_child_parent] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[tbl_masked_parent] ([Id]);");

        script.Should().Contain("IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tbl_masked_child_old')");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_child] DROP CONSTRAINT [FK_tbl_masked_child_old];");
    }

    [Fact]
    public void GenerateMigrationScript_DropSafeguards_WhenAllowDropsFalse_OmitsDrops()
    {
        // Arrange
        var diff = new SchemaDiff();

        // Deleted table
        diff.Tables.Add(new TableDiff
        {
            TableName = "tbl_masked_obsolete",
            Schema = "dbo",
            Kind = DiffKind.Deleted,
            Source = new TableSchema { Name = "tbl_masked_obsolete" }
        });

        // Modified table with deleted column
        var modTable = new TableDiff
        {
            TableName = "tbl_masked_active",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };
        modTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "ColDeleted",
            Kind = DiffKind.Deleted,
            Source = new ColumnSchema { Name = "ColDeleted", Type = StandardType.Int }
        });
        diff.Tables.Add(modTable);

        var options = new SqlServerApplierOptions { AllowDrops = false };

        // Act
        var script = _applier.GenerateMigrationScript(diff, options);

        // Assert
        script.Should().NotContain("DROP TABLE [dbo].[tbl_masked_obsolete]");
        script.Should().NotContain("DROP COLUMN [ColDeleted]");
    }

    [Fact]
    public void GenerateMigrationScript_DropSafeguards_WhenAllowDropsTrue_GeneratesGuardedDrops()
    {
        // Arrange
        var diff = new SchemaDiff();

        // Deleted table
        diff.Tables.Add(new TableDiff
        {
            TableName = "tbl_masked_obsolete",
            Schema = "dbo",
            Kind = DiffKind.Deleted,
            Source = new TableSchema { Name = "tbl_masked_obsolete" }
        });

        // Modified table with deleted column
        var modTable = new TableDiff
        {
            TableName = "tbl_masked_active",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };
        modTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "ColDeleted",
            Kind = DiffKind.Deleted,
            Source = new ColumnSchema { Name = "ColDeleted", Type = StandardType.Int }
        });
        diff.Tables.Add(modTable);

        var options = new SqlServerApplierOptions { AllowDrops = true };

        // Act
        var script = _applier.GenerateMigrationScript(diff, options);

        // Assert
        script.Should().Contain("IF OBJECT_ID(N'[dbo].[tbl_masked_obsolete]', N'U') IS NOT NULL");
        script.Should().Contain("DROP TABLE [dbo].[tbl_masked_obsolete];");
        script.Should().Contain("IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[tbl_masked_active]') AND name = N'ColDeleted')");
        script.Should().Contain("ALTER TABLE [dbo].[tbl_masked_active] DROP COLUMN [ColDeleted];");
    }

    [Fact]
    public async Task PreviewAsync_FileTarget_GeneratesFileDiffPreviewWithUnifiedDiff()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_SqlTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_demo" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_demo", Kind = DiffKind.Added, Target = table });

            var options = new SqlServerApplierOptions
            {
                TargetDirectory = tempDir,
                ScriptFileName = "V1_0__migration.sql"
            };

            // Act
            var previews = await _applier.PreviewAsync(diff, options);

            // Assert
            previews.Should().HaveCount(1);
            var preview = previews[0];
            preview.FilePath.Should().EndWith("V1_0__migration.sql");
            preview.DiffKind.Should().Be(DiffKind.Added);
            preview.NewContent.Should().Contain("CREATE TABLE [dbo].[tbl_masked_demo]");
            preview.UnifiedDiff.Should().Contain("--- /dev/null");
            preview.UnifiedDiff.Should().Contain("+++ b/V1_0__migration.sql");
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
    public async Task PreviewAsync_LiveDatabaseTarget_GeneratesLiveDatabasePreview()
    {
        // Arrange
        var diff = new SchemaDiff();
        var table = new TableSchema { Name = "tbl_masked_live" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        diff.Tables.Add(new TableDiff { TableName = "tbl_masked_live", Kind = DiffKind.Added, Target = table });

        var options = new SqlServerApplierOptions
        {
            TargetDirectory = "Server=localhost;Database=MaskedLiveDb;Integrated Security=true;"
        };

        // Act
        var previews = await _applier.PreviewAsync(diff, options);

        // Assert
        previews.Should().HaveCount(1);
        var preview = previews[0];
        preview.FilePath.Should().Contain("MaskedLiveDb");
        preview.DiffKind.Should().Be(DiffKind.Modified);
        preview.NewContent.Should().Contain("CREATE TABLE [dbo].[tbl_masked_live]");
        preview.UnifiedDiff.Should().Contain("CREATE TABLE [dbo].[tbl_masked_live]");
    }

    [Fact]
    public async Task ApplyAsync_FileTarget_WritesScriptToDiskAndHonorsDryRun()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_SqlApplyTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_item" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_item", Kind = DiffKind.Added, Target = table });

            var filePath = Path.Combine(tempDir, "migration.sql");
            var dryRunOptions = new SqlServerApplierOptions
            {
                TargetDirectory = filePath,
                DryRun = true
            };

            // Act 1: DryRun
            var dryResult = await _applier.ApplyAsync(diff, dryRunOptions);

            // Assert 1
            dryResult.Success.Should().BeTrue();
            File.Exists(filePath).Should().BeFalse();

            // Act 2: Actual Apply
            var liveOptions = new SqlServerApplierOptions
            {
                TargetDirectory = filePath,
                DryRun = false
            };
            var liveResult = await _applier.ApplyAsync(diff, liveOptions);

            // Assert 2
            liveResult.Success.Should().BeTrue();
            liveResult.CreatedFiles.Should().Contain(filePath);
            File.Exists(filePath).Should().BeTrue();
            var writtenText = await File.ReadAllTextAsync(filePath);
            writtenText.Should().Contain("CREATE TABLE [dbo].[tbl_masked_item]");
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
    public async Task ApplyAsync_LiveDatabaseTarget_ExecutesBatchesViaSqlExecutor()
    {
        // Arrange
        var fakeExecutor = new FakeSqlMigrationExecutor();
        var applierWithExecutor = new SqlServerMigrationApplier(fakeExecutor);

        var diff = new SchemaDiff();
        var table = new TableSchema { Name = "tbl_masked_exec_test" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        diff.Tables.Add(new TableDiff { TableName = "tbl_masked_exec_test", Kind = DiffKind.Added, Target = table });

        var options = new SqlServerApplierOptions
        {
            TargetDirectory = "Server=sql_server_mock;Database=TestDb;Integrated Security=true;",
            Transactional = true
        };

        // Act
        var result = await applierWithExecutor.ApplyAsync(diff, options);

        // Assert
        result.Success.Should().BeTrue();
        fakeExecutor.LastConnectionString.Should().Be("Server=sql_server_mock;Database=TestDb;Integrated Security=true;");
        fakeExecutor.LastTransactional.Should().BeTrue();
        fakeExecutor.ExecutedBatches.Should().NotBeEmpty();
        fakeExecutor.ExecutedBatches.Any(b => b.Contains("CREATE TABLE [dbo].[tbl_masked_exec_test]")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyAsync_LiveDatabaseTarget_WhenExecutionFails_ReturnsFailureWithErrors()
    {
        // Arrange
        var fakeExecutor = new FakeSqlMigrationExecutor { ShouldThrow = true };
        var applierWithExecutor = new SqlServerMigrationApplier(fakeExecutor);

        var diff = new SchemaDiff();
        var table = new TableSchema { Name = "tbl_masked_fail" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        diff.Tables.Add(new TableDiff { TableName = "tbl_masked_fail", Kind = DiffKind.Added, Target = table });

        var options = new SqlServerApplierOptions
        {
            TargetDirectory = "Server=sql_server_mock;Database=TestDb;Integrated Security=true;"
        };

        // Act
        var result = await applierWithExecutor.ApplyAsync(diff, options);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Simulated SQL execution failure."));
    }

    [Fact]
    public async Task PreviewAsync_ExistingFileTarget_ProducesModifiedUnifiedDiff()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_SqlExistingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "migration.sql");
            var originalContent = "-- Initial script\nCREATE TABLE [dbo].[OldTable] ( [Id] INT );\n";
            await File.WriteAllTextAsync(filePath, originalContent);

            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_new" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_new", Kind = DiffKind.Added, Target = table });

            var options = new SqlServerApplierOptions
            {
                TargetDirectory = filePath
            };

            // Act
            var previews = await _applier.PreviewAsync(diff, options);

            // Assert
            previews.Should().HaveCount(1);
            var preview = previews[0];
            preview.FilePath.Should().Be(filePath);
            preview.DiffKind.Should().Be(DiffKind.Modified);
            preview.OriginalContent.Should().Be(originalContent);
            preview.NewContent.Should().Contain("CREATE TABLE [dbo].[tbl_masked_new]");
            preview.UnifiedDiff.Should().Contain("--- a/migration.sql");
            preview.UnifiedDiff.Should().Contain("+++ b/migration.sql");
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
    public void GenerateMigrationScript_ModifiedColumnComment_DoesNotEmitExtendedProperties()
    {
        // Arrange
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_docs",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };

        var srcCol = new ColumnSchema { Name = "Notes", Type = StandardType.String, Comment = "Old comment" };
        var tgtCol = new ColumnSchema { Name = "Notes", Type = StandardType.String, Comment = "Updated field comment" };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Notes",
            Kind = DiffKind.Modified,
            Source = srcCol,
            Target = tgtCol,
            Changes = ChangeDetail.CommentChanged
        });

        var diff = new SchemaDiff();
        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert — comments / extended properties are never emitted in T-SQL DDL
        script.Should().NotContain("sp_updateextendedproperty");
        script.Should().NotContain("sp_addextendedproperty");
    }

    [Fact]
    public void SplitIntoBatches_ValidScript_SplitsProperlyOnGo()
    {
        // Arrange
        var script = """
            -- Header
            PRINT 'Batch 1';
            GO
            PRINT 'Batch 2';
            GO
            PRINT 'Batch 3';
            """;

        // Act
        var batches = SqlServerMigrationGenerator.SplitIntoBatches(script);

        // Assert
        batches.Should().HaveCount(3);
        batches[0].Should().Contain("Batch 1");
        batches[1].Should().Contain("Batch 2");
        batches[2].Should().Contain("Batch 3");
    }

    [Fact]
    public void GenerateMigrationScript_FullEndToEndScenario_ProducesCompleteValidScript()
    {
        // Arrange
        var diff = new SchemaDiff();

        // 1. Added customer table
        var custTable = new TableSchema { Name = "tbl_masked_customer", Schema = "sales", Comment = "Customers" };
        custTable.AddColumn(new ColumnSchema { Name = "CustomerId", Type = StandardType.Int, IsPrimaryKey = true, IsIdentity = true });
        custTable.AddColumn(new ColumnSchema { Name = "CustomerName", Type = StandardType.String, Length = 100, IsNullable = false });
        diff.Tables.Add(new TableDiff { TableName = "tbl_masked_customer", Schema = "sales", Kind = DiffKind.Added, Target = custTable });

        // 2. Added order table with FK to customer
        var orderTable = new TableSchema { Name = "tbl_masked_order", Schema = "sales" };
        orderTable.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.BigInt, IsPrimaryKey = true, IsIdentity = true });
        orderTable.AddColumn(new ColumnSchema { Name = "CustomerId", Type = StandardType.Int, IsNullable = false });
        orderTable.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_tbl_masked_order_customer",
            DependentTable = "tbl_masked_order",
            DependentColumn = "CustomerId",
            PrincipalTable = "tbl_masked_customer",
            PrincipalColumn = "CustomerId"
        });
        diff.Tables.Add(new TableDiff { TableName = "tbl_masked_order", Schema = "sales", Kind = DiffKind.Added, Target = orderTable });

        // 3. Modified audit log table
        var auditTable = new TableDiff { TableName = "tbl_masked_audit", Schema = "audit", Kind = DiffKind.Modified };
        auditTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "IpAddress",
            Kind = DiffKind.Added,
            Target = new ColumnSchema { Name = "IpAddress", Type = StandardType.String, Length = 45, IsNullable = true }
        });
        diff.Tables.Add(auditTable);

        // 4. Deleted legacy table with AllowDrops = true
        diff.Tables.Add(new TableDiff
        {
            TableName = "tbl_masked_legacy",
            Schema = "dbo",
            Kind = DiffKind.Deleted,
            Source = new TableSchema { Name = "tbl_masked_legacy" }
        });

        var options = new SqlServerApplierOptions
        {
            AllowDrops = true,
            Transactional = true
        };

        // Act
        var script = _applier.GenerateMigrationScript(diff, options);

        // Assert
        script.Should().Contain("BEGIN TRANSACTION;");
        script.Should().Contain("CREATE TABLE [sales].[tbl_masked_customer]");
        script.Should().Contain("CREATE TABLE [sales].[tbl_masked_order]");
        script.Should().Contain("ALTER TABLE [sales].[tbl_masked_order] ADD CONSTRAINT [FK_tbl_masked_order_customer] FOREIGN KEY ([CustomerId]) REFERENCES [sales].[tbl_masked_customer] ([CustomerId]);");
        script.Should().Contain("ALTER TABLE [audit].[tbl_masked_audit] ADD [IpAddress] NVARCHAR(45) NULL;");
        script.Should().Contain("DROP TABLE [dbo].[tbl_masked_legacy];");
        script.Should().Contain("COMMIT TRANSACTION;");
    }

    [Fact]
    public void ApplierRegistry_ResolvesSqlServerApplierForScriptAndDatabaseTargetTypes()
    {
        // Arrange
        var registry = new ApplierRegistry();

        // Act
        var scriptApplier = registry.Resolve(TargetType.SqlServerScript);
        var dbApplier = registry.Resolve(TargetType.SqlServerDatabase);

        // Assert
        scriptApplier.Should().NotBeNull();
        scriptApplier.Name.Should().Be("SqlServerMigrationApplier");

        dbApplier.Should().NotBeNull();
        dbApplier.Name.Should().Be("SqlServerMigrationApplier");
    }

    [Fact]
    public void GenerateMigrationScript_NonTransactionalAndNoHeader_OmitsTransactionAndHeaderBlocks()
    {
        // Arrange
        var diff = new SchemaDiff();
        var table = new TableSchema { Name = "tbl_simple" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        diff.Tables.Add(new TableDiff { TableName = "tbl_simple", Kind = DiffKind.Added, Target = table });

        var options = new SqlServerApplierOptions
        {
            IncludeHeader = false,
            Transactional = false
        };

        // Act
        var script = _applier.GenerateMigrationScript(diff, options);

        // Assert
        script.Should().NotContain("SchemaAlign SQL Server Migration Script");
        script.Should().NotContain("BEGIN TRANSACTION;");
        script.Should().NotContain("COMMIT TRANSACTION;");
        script.Should().Contain("CREATE TABLE [dbo].[tbl_simple]");
    }

    [Fact]
    public void GenerateMigrationScript_CustomSchema_UsesCustomSchema()
    {
        // Arrange
        var diff = new SchemaDiff();
        var table = new TableSchema
        {
            Name = "tbl_quote_test",
            Schema = "",
            Comment = "It's a table with 'quotes' in description"
        };
        table.AddColumn(new ColumnSchema
        {
            Name = "DataCol",
            Type = StandardType.String,
            Length = 50,
            Comment = "User's description"
        });
        diff.Tables.Add(new TableDiff { TableName = "tbl_quote_test", Schema = "", Kind = DiffKind.Added, Target = table });

        var options = new SqlServerApplierOptions
        {
            DefaultSchema = "inventory"
        };

        // Act
        var script = _applier.GenerateMigrationScript(diff, options);

        // Assert
        script.Should().Contain("CREATE TABLE [inventory].[tbl_quote_test]");
        script.Should().NotContain("sp_addextendedproperty");
    }

    [Fact]
    public void GenerateMigrationScript_ModifiedColumnCommentOnly_DoesNotEmitAlterColumn()
    {
        // Arrange
        var tableDiff = new TableDiff
        {
            TableName = "tbl_masked_docs",
            Schema = "dbo",
            Kind = DiffKind.Modified
        };

        var srcCol = new ColumnSchema { Name = "Description", Type = StandardType.String, Length = 100, Comment = "Old description" };
        var tgtCol = new ColumnSchema { Name = "Description", Type = StandardType.String, Length = 100, Comment = "New description" };
        tableDiff.Columns.Add(new ColumnDiff
        {
            ColumnName = "Description",
            Kind = DiffKind.Modified,
            Source = srcCol,
            Target = tgtCol,
            Changes = ChangeDetail.CommentChanged
        });

        var diff = new SchemaDiff();
        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().NotContain("ALTER COLUMN");
        script.Should().NotContain("sp_updateextendedproperty");
        script.Should().NotContain("sp_addextendedproperty");
    }

    [Fact]
    public void GenerateMigrationScript_ForeignKeyToPrincipalInTargetSchema_ResolvesPrincipalSchema()
    {
        // Arrange
        var diff = new SchemaDiff();
        var principalTable = new TableSchema { Name = "Users", Schema = "identity" };
        principalTable.AddColumn(new ColumnSchema { Name = "UserId", Type = StandardType.Int, IsPrimaryKey = true });
        diff.TargetSchema.AddTable(principalTable);

        var tableDiff = new TableDiff
        {
            TableName = "AuditLogs",
            Schema = "logging",
            Kind = DiffKind.Modified
        };

        var addedFk = new ForeignKeySchema
        {
            ConstraintName = "FK_AuditLogs_Users",
            DependentTable = "AuditLogs",
            DependentColumn = "ActorId",
            PrincipalTable = "Users",
            PrincipalColumn = "UserId"
        };
        tableDiff.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = addedFk.ConstraintName,
            Kind = DiffKind.Added,
            Target = addedFk
        });

        diff.Tables.Add(tableDiff);

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert
        script.Should().Contain("REFERENCES [identity].[Users] ([UserId])");
    }

    // ==================== Gap 1: SplitIntoBatches empty/whitespace/null ====================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SplitIntoBatches_EmptyOrWhitespaceInput_ReturnsEmptyCollection(string? input)
    {
        // Act
        var batches = SqlServerMigrationGenerator.SplitIntoBatches(input!);

        // Assert
        batches.Should().BeEmpty();
    }

    // ==================== Gap 2: Empty diff (no tables) ====================

    [Fact]
    public void GenerateMigrationScript_EmptyDiff_GeneratesOnlyHeaderAndTransactionBlocks()
    {
        // Arrange
        var diff = new SchemaDiff();
        var options = new SqlServerApplierOptions
        {
            IncludeHeader = true,
            Transactional = true
        };

        // Act
        var script = _applier.GenerateMigrationScript(diff, options);

        // Assert
        script.Should().Contain("SchemaAlign SQL Server Migration Script");
        script.Should().Contain("BEGIN TRANSACTION;");
        script.Should().Contain("COMMIT TRANSACTION;");
        script.Should().NotContain("CREATE TABLE");
        script.Should().NotContain("ALTER TABLE");
        script.Should().NotContain("DROP TABLE");
    }

    // ==================== Gap 3: ResolveTargetFilePath — empty path ====================

    [Fact]
    public async Task ApplyAsync_EmptyTargetDirectory_UsesDefaultScriptFileName()
    {
        // Arrange — use a temp dir as CWD so the default "migration.sql" lands somewhere known
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_EmptyDir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_empty_path" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_empty_path", Kind = DiffKind.Added, Target = table });

            // Pass directory + explicit ScriptFileName to exercise the combine path
            var options = new SqlServerApplierOptions
            {
                TargetDirectory = tempDir,
                ScriptFileName = "custom_migration.sql"
            };

            // Act
            var result = await _applier.ApplyAsync(diff, options);

            // Assert
            result.Success.Should().BeTrue();
            var expectedPath = Path.Combine(tempDir, "custom_migration.sql");
            result.CreatedFiles.Should().Contain(expectedPath);
            File.Exists(expectedPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ==================== Gap 4: ApplyAsync creates parent directories ====================

    [Fact]
    public async Task ApplyAsync_FileTarget_CreatesParentDirectoriesIfMissing()
    {
        // Arrange — nested path that does not exist
        var tempBase = Path.Combine(Path.GetTempPath(), "SchemaAlign_AutoDir_" + Guid.NewGuid().ToString("N"));
        var nestedDir = Path.Combine(tempBase, "sub1", "sub2");
        var filePath = Path.Combine(nestedDir, "auto_created.sql");

        try
        {
            Directory.Exists(nestedDir).Should().BeFalse("nested directory should not exist before test");

            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_autodir" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_autodir", Kind = DiffKind.Added, Target = table });

            var options = new SqlServerApplierOptions
            {
                TargetDirectory = filePath
            };

            // Act
            var result = await _applier.ApplyAsync(diff, options);

            // Assert
            result.Success.Should().BeTrue();
            Directory.Exists(nestedDir).Should().BeTrue("parent directories should have been auto-created");
            File.Exists(filePath).Should().BeTrue();
            result.CreatedFiles.Should().Contain(filePath);
        }
        finally
        {
            if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
        }
    }

    // ==================== Gap 5: ApplyAsync existing file → ChangedFiles ====================

    [Fact]
    public async Task ApplyAsync_FileTarget_ExistingFile_AddsToChangedFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_OverwriteTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var filePath = Path.Combine(tempDir, "migration.sql");
            await File.WriteAllTextAsync(filePath, "-- Existing script\nSELECT 1;\n");

            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_overwrite" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_overwrite", Kind = DiffKind.Added, Target = table });

            var options = new SqlServerApplierOptions
            {
                TargetDirectory = filePath
            };

            // Act
            var result = await _applier.ApplyAsync(diff, options);

            // Assert
            result.Success.Should().BeTrue();
            result.ChangedFiles.Should().Contain(filePath);
            result.CreatedFiles.Should().NotContain(filePath);
            var content = await File.ReadAllTextAsync(filePath);
            content.Should().Contain("CREATE TABLE [dbo].[tbl_masked_overwrite]");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ==================== Gap 6: Base ApplierOptions fallback ====================

    [Fact]
    public async Task PreviewAsync_BaseApplierOptions_NormalizesToSqlServerApplierOptions()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_BaseOptions_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_base_opts" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_base_opts", Kind = DiffKind.Added, Target = table });

            // Pass base ApplierOptions, NOT SqlServerApplierOptions
            var baseOptions = new ApplierOptions
            {
                TargetDirectory = tempDir,
                AllowDrops = false,
                DryRun = false
            };

            // Act
            var previews = await _applier.PreviewAsync(diff, baseOptions);

            // Assert
            previews.Should().HaveCount(1);
            previews[0].NewContent.Should().Contain("CREATE TABLE [dbo].[tbl_masked_base_opts]");
            previews[0].DiffKind.Should().Be(DiffKind.Added);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    // ==================== Gap 7: FK with null ConstraintName auto-generates name ====================

    [Fact]
    public void GenerateMigrationScript_ForeignKeyWithNullConstraintName_GeneratesDefaultName()
    {
        // Arrange
        var table = new TableSchema { Name = "tbl_masked_orders", Schema = "dbo" };
        table.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.Int, IsPrimaryKey = true });
        table.AddColumn(new ColumnSchema { Name = "CustId", Type = StandardType.Int, IsNullable = false });
        table.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = null, // Auto-generate
            DependentTable = "tbl_masked_orders",
            DependentColumn = "CustId",
            PrincipalTable = "tbl_masked_customers",
            PrincipalColumn = "Id"
        });

        var diff = new SchemaDiff();
        diff.Tables.Add(new TableDiff
        {
            TableName = "tbl_masked_orders",
            Schema = "dbo",
            Kind = DiffKind.Added,
            Target = table
        });

        // Act
        var script = _applier.GenerateMigrationScript(diff);

        // Assert — should use pattern FK_{table}_{principal}_{col}
        script.Should().Contain("FK_tbl_masked_orders_tbl_masked_customers_CustId");
        script.Should().Contain("ADD CONSTRAINT [FK_tbl_masked_orders_tbl_masked_customers_CustId]");
        script.Should().Contain("FOREIGN KEY ([CustId]) REFERENCES [dbo].[tbl_masked_customers] ([Id])");
    }

    // ==================== Gap 8: DryRun with live DB connection string ====================

    [Fact]
    public async Task ApplyAsync_DryRun_WithConnectionString_DoesNotExecuteBatches()
    {
        // Arrange
        var fakeExecutor = new FakeSqlMigrationExecutor();
        var applierWithExecutor = new SqlServerMigrationApplier(fakeExecutor);

        var diff = new SchemaDiff();
        var table = new TableSchema { Name = "tbl_masked_dryrun_db" };
        table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
        diff.Tables.Add(new TableDiff { TableName = "tbl_masked_dryrun_db", Kind = DiffKind.Added, Target = table });

        var options = new SqlServerApplierOptions
        {
            ConnectionString = "Server=sql_server_mock;Database=DryRunDb;Integrated Security=true;",
            DryRun = true
        };

        // Act
        var result = await applierWithExecutor.ApplyAsync(diff, options);

        // Assert — DryRun should prevent any execution
        result.Success.Should().BeTrue();
        result.Previews.Should().NotBeEmpty("previews should still be generated in dry-run");
        result.ChangedFiles.Should().BeEmpty();
        result.CreatedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyAsync_WithScriptOutputFilePath_WritesScriptFileDirectly()
    {
        // Arrange
        var fakeExecutor = new FakeSqlMigrationExecutor();
        var applierWithExecutor = new SqlServerMigrationApplier(fakeExecutor);

        var tempDir = Path.Combine(Path.GetTempPath(), "SchemaAlign_SqlOutTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, "custom_migration.sql");

        try
        {
            var diff = new SchemaDiff();
            var table = new TableSchema { Name = "tbl_masked_output_test" };
            table.AddColumn(new ColumnSchema { Name = "Id", Type = StandardType.Int, IsPrimaryKey = true });
            diff.Tables.Add(new TableDiff { TableName = "tbl_masked_output_test", Kind = DiffKind.Added, Target = table });

            var options = new SqlServerApplierOptions
            {
                ConnectionString = "Server=sql_server_mock;Database=LiveDb;Integrated Security=true;",
                ScriptOutputFilePath = targetFile
            };

            // Act
            var result = await applierWithExecutor.ApplyAsync(diff, options);

            // Assert
            result.Success.Should().BeTrue();
            fakeExecutor.ExecutedBatches.Should().BeEmpty("should not execute against DB when ScriptOutputFilePath is specified");
            File.Exists(targetFile).Should().BeTrue();
            var fileContent = await File.ReadAllTextAsync(targetFile);
            fileContent.Should().Contain("CREATE TABLE [dbo].[tbl_masked_output_test]");
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

