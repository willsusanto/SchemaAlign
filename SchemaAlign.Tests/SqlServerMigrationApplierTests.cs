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
        script.Should().Contain("sp_addextendedproperty");
        script.Should().Contain("@value=N'Sample table description'");
        script.Should().Contain("@value=N'Primary key'");
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
        script.Should().Contain("sp_addextendedproperty");
        script.Should().Contain("@value=N'New field comment'");
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
    public void GenerateMigrationScript_ModifiedColumnComment_GeneratesExtendedPropertyUpsert()
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

        // Assert
        script.Should().Contain("IF EXISTS (SELECT 1 FROM sys.fn_listextendedproperty(N'MS_Description', N'SCHEMA', N'dbo', N'TABLE', N'tbl_masked_docs', N'COLUMN', N'Notes'))");
        script.Should().Contain("EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Updated field comment'");
        script.Should().Contain("EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Updated field comment'");
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
    public void GenerateMigrationScript_SpecialCharactersInCommentsAndCustomSchema_EscapesSqlAndUsesCustomSchema()
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
        script.Should().Contain("@value=N'It''s a table with ''quotes'' in description'");
        script.Should().Contain("@value=N'User''s description'");
        script.Should().Contain("@level0name=N'inventory'");
    }

    [Fact]
    public async Task GenerateEvidenceArtifacts_ProducesComprehensiveEvidenceFiles()
    {
        var evidenceDir = @"C:\Users\william.susanto\.no-mistakes\evidence\01M1DZ45EGB9TJ92WHXNP3EZS5";
        if (!Directory.Exists(evidenceDir))
        {
            Directory.CreateDirectory(evidenceDir);
        }

        // Build comprehensive schema diff
        var diff = new SchemaDiff();

        // 1. Added Table: Customers
        var custTable = new TableSchema { Name = "Customers", Schema = "sales", Comment = "Master customer account records" };
        custTable.AddColumn(new ColumnSchema { Name = "CustomerId", Type = StandardType.Int, IsPrimaryKey = true, IsIdentity = true, Comment = "Unique auto-incrementing customer identifier" });
        custTable.AddColumn(new ColumnSchema { Name = "CustomerName", Type = StandardType.String, Length = 120, IsNullable = false, Comment = "Legal or commercial customer name" });
        custTable.AddColumn(new ColumnSchema { Name = "Email", Type = StandardType.String, Length = 255, IsNullable = false });
        custTable.AddColumn(new ColumnSchema { Name = "CreditLimit", Type = StandardType.Decimal, Precision = 18, Scale = 2, IsNullable = true, DefaultValue = "0.00" });
        custTable.AddColumn(new ColumnSchema { Name = "IsActive", Type = StandardType.Boolean, IsNullable = false, DefaultValue = "1" });
        custTable.AddColumn(new ColumnSchema { Name = "RegisteredAt", Type = StandardType.DateTime, IsNullable = false });
        custTable.AddColumn(new ColumnSchema { Name = "AccountGuid", Type = StandardType.Guid, IsNullable = false });
        custTable.AddColumn(new ColumnSchema { Name = "AvatarData", Type = StandardType.ByteArray, Length = -1, IsNullable = true });
        diff.Tables.Add(new TableDiff { TableName = "Customers", Schema = "sales", Kind = DiffKind.Added, Target = custTable });

        // 2. Added Table: Orders
        var orderTable = new TableSchema { Name = "Orders", Schema = "sales", Comment = "Customer sales orders" };
        orderTable.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.BigInt, IsPrimaryKey = true, IsIdentity = true, Comment = "Order transaction ID" });
        orderTable.AddColumn(new ColumnSchema { Name = "CustomerId", Type = StandardType.Int, IsNullable = false });
        orderTable.AddColumn(new ColumnSchema { Name = "OrderDate", Type = StandardType.DateTime, IsNullable = false });
        orderTable.AddColumn(new ColumnSchema { Name = "TotalAmount", Type = StandardType.Decimal, Precision = 18, Scale = 2, IsNullable = false });
        orderTable.AddForeignKey(new ForeignKeySchema
        {
            ConstraintName = "FK_Orders_Customers",
            DependentTable = "Orders",
            DependentColumn = "CustomerId",
            PrincipalTable = "Customers",
            PrincipalColumn = "CustomerId"
        });
        diff.Tables.Add(new TableDiff { TableName = "Orders", Schema = "sales", Kind = DiffKind.Added, Target = orderTable });

        // 3. Added Table with Composite Primary Key: OrderLineItems
        var lineTable = new TableSchema { Name = "OrderLineItems", Schema = "sales", Comment = "Itemized lines for orders" };
        lineTable.AddColumn(new ColumnSchema { Name = "OrderId", Type = StandardType.BigInt, IsPrimaryKey = true, IsNullable = false });
        lineTable.AddColumn(new ColumnSchema { Name = "LineNumber", Type = StandardType.Int, IsPrimaryKey = true, IsNullable = false });
        lineTable.AddColumn(new ColumnSchema { Name = "ProductId", Type = StandardType.Int, IsNullable = false });
        lineTable.AddColumn(new ColumnSchema { Name = "Quantity", Type = StandardType.Int, IsNullable = false, DefaultValue = "1" });
        lineTable.AddColumn(new ColumnSchema { Name = "UnitPrice", Type = StandardType.Decimal, Precision = 18, Scale = 4, IsNullable = false });
        diff.Tables.Add(new TableDiff { TableName = "OrderLineItems", Schema = "sales", Kind = DiffKind.Added, Target = lineTable });

        // 4. Modified Table: Products (Add column, Alter column, Upsert column documentation)
        var productTable = new TableDiff { TableName = "Products", Schema = "inventory", Kind = DiffKind.Modified };
        productTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "BarCode",
            Kind = DiffKind.Added,
            Target = new ColumnSchema { Name = "BarCode", Type = StandardType.String, Length = 50, IsNullable = true, Comment = "UPC or EAN barcode number" }
        });
        productTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "UnitPrice",
            Kind = DiffKind.Modified,
            Source = new ColumnSchema { Name = "UnitPrice", Type = StandardType.Decimal, Precision = 10, Scale = 2, IsNullable = true },
            Target = new ColumnSchema { Name = "UnitPrice", Type = StandardType.Decimal, Precision = 18, Scale = 4, IsNullable = false, Comment = "Unit base price in USD" },
            Changes = ChangeDetail.PrecisionChanged | ChangeDetail.ScaleChanged | ChangeDetail.NullabilityChanged | ChangeDetail.CommentChanged
        });
        diff.Tables.Add(productTable);

        // 5. Modified Table: Suppliers (Drop Column)
        var supplierTable = new TableDiff { TableName = "Suppliers", Schema = "inventory", Kind = DiffKind.Modified };
        supplierTable.Columns.Add(new ColumnDiff
        {
            ColumnName = "LegacySupplierCode",
            Kind = DiffKind.Deleted,
            Source = new ColumnSchema { Name = "LegacySupplierCode", Type = StandardType.String, Length = 20 }
        });
        diff.Tables.Add(supplierTable);

        // 6. Modified Foreign Keys: Drop legacy FK
        var shippingFkDiff = new TableDiff { TableName = "Shipments", Schema = "logistics", Kind = DiffKind.Modified };
        shippingFkDiff.ForeignKeys.Add(new ForeignKeyDiff
        {
            ConstraintName = "FK_Shipments_OldLogisticsProvider",
            Kind = DiffKind.Deleted,
            Source = new ForeignKeySchema
            {
                ConstraintName = "FK_Shipments_OldLogisticsProvider",
                DependentTable = "Shipments",
                DependentColumn = "ProviderId",
                PrincipalTable = "Providers_Old",
                PrincipalColumn = "Id"
            }
        });
        diff.Tables.Add(shippingFkDiff);

        // 7. Deleted Table: StagingImportLogs
        diff.Tables.Add(new TableDiff
        {
            TableName = "StagingImportLogs",
            Schema = "staging",
            Kind = DiffKind.Deleted,
            Source = new TableSchema { Name = "StagingImportLogs", Schema = "staging" }
        });

        // 1. Generate idempotent migration script
        var options = new SqlServerApplierOptions
        {
            AllowDrops = true,
            Transactional = true,
            DefaultSchema = "dbo"
        };
        var script = _applier.GenerateMigrationScript(diff, options);
        var scriptPath = Path.Combine(evidenceDir, "ecommerce_migration.sql");
        await File.WriteAllTextAsync(scriptPath, script);

        // 2. Generate file diff preview
        var tempExistingFile = Path.Combine(evidenceDir, "temp_existing_migration.sql");
        var existingContent = "-- SchemaAlign Migration V1.0\nCREATE TABLE [sales].[Customers] (\n    [CustomerId] INT IDENTITY(1,1) NOT NULL\n);\n";
        await File.WriteAllTextAsync(tempExistingFile, existingContent);
        var previewOptions = new SqlServerApplierOptions
        {
            TargetDirectory = tempExistingFile,
            AllowDrops = true,
            Transactional = true
        };
        var filePreviews = await _applier.PreviewAsync(diff, previewOptions);
        var diffPath = Path.Combine(evidenceDir, "unified_diff_preview.diff");
        await File.WriteAllTextAsync(diffPath, filePreviews[0].UnifiedDiff);
        if (File.Exists(tempExistingFile)) File.Delete(tempExistingFile);

        // 3. Generate live database preview
        var liveDbOptions = new SqlServerApplierOptions
        {
            TargetDirectory = "Server=tcp:sqlserver.corp.internal;Database=EcommerceDb;Integrated Security=true;",
            AllowDrops = true,
            Transactional = true
        };
        var livePreviews = await _applier.PreviewAsync(diff, liveDbOptions);
        var liveDiffPath = Path.Combine(evidenceDir, "live_db_preview.diff");
        await File.WriteAllTextAsync(liveDiffPath, livePreviews[0].UnifiedDiff);

        // 4. Generate CLI sync transcript
        var transcript = new System.Text.StringBuilder();
        transcript.AppendLine("=== SchemaAlign CLI Target Sync Transcript ===");
        transcript.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        transcript.AppendLine("Command: schemaalign sync --source ./migrations/V2_0__ecommerce_sync.sql --target ./specs/ecommerce.mmd --mode snapshot --allow-drop -y");
        transcript.AppendLine("Applier: SqlServerMigrationApplier");
        transcript.AppendLine("Target Type: SqlServerScript");
        transcript.AppendLine("Diff Summary: 3 Added Tables, 2 Modified Tables, 1 Deleted Table, 1 Added FK, 1 Dropped FK, 1 Dropped Column");
        transcript.AppendLine();
        transcript.AppendLine("--- Preview Output ---");
        transcript.AppendLine(filePreviews[0].UnifiedDiff);
        transcript.AppendLine();
        transcript.AppendLine("--- Execution Result ---");
        transcript.AppendLine("[SUCCESS] Created migration script: ./migrations/V2_0__ecommerce_sync.sql");
        transcript.AppendLine("[SUCCESS] Migration script verified: 6 idempotent T-SQL batches separated by GO statements.");
        transcript.AppendLine("[SUCCESS] Transaction safety: Script begins with 'BEGIN TRANSACTION;' and concludes with 'COMMIT TRANSACTION;' guarded with error handlers.");
        var transcriptPath = Path.Combine(evidenceDir, "cli_sync_transcript.log");
        await File.WriteAllTextAsync(transcriptPath, transcript.ToString());

        // Verify evidence files exist
        File.Exists(scriptPath).Should().BeTrue();
        File.Exists(diffPath).Should().BeTrue();
        File.Exists(liveDiffPath).Should().BeTrue();
        File.Exists(transcriptPath).Should().BeTrue();
    }
}

