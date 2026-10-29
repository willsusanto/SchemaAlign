using SchemaAlign.Appliers.SqlServer;
using SchemaAlign.Diff;
using SchemaAlign.Models;
using SchemaAlign.Readers.Mermaid;

namespace SchemaAlign.Tests;

public class SqlForeignKeyRegressionTests
{
    [Fact]
    public void Read_UnrelatedKeysSharingPrefix_DoNotBecomeRoleReferences()
    {
        var schema = new MermaidSchemaReader().Read("""
            erDiagram
                MsActor { string IdActor PK }
                MsAsset { string IdAsset PK }
                LtPermission {
                    string IdPermission PK
                    string IdActorCategory FK
                }
                TrEntry {
                    string IdEntry PK
                    string IdAssetPurpose FK
                    string IdAssetSource FK
                }
            """);

        schema.Tables["LtPermission"].ForeignKeys.Should().BeEmpty();
        schema.Tables["TrEntry"].ForeignKeys.Should().BeEmpty();
        var diff = SchemaDiffCalculator.Calculate(new DatabaseSchema(), schema);
        SqlServerMigrationGenerator.Generate(diff).Should().NotContain("FOREIGN KEY");
    }

    [Fact]
    public void Read_InferredExactKeyDoesNotEstablishRoleEvidence()
    {
        var schema = new MermaidSchemaReader().Read("""
            erDiagram
                MsActor { string IdActor PK }
                TrEntry {
                    string IdEntry PK
                    string IdActor FK
                    string IdActorCategory FK
                }
            """);

        schema.Tables["TrEntry"].ForeignKeys.Should().ContainSingle()
            .Which.DependentColumn.Should().Be("IdActor");
    }

    [Fact]
    public void Read_ExactKeyTakesPrecedenceOverRolePrefix()
    {
        var schema = new MermaidSchemaReader().Read("""
            erDiagram
                MsActor { string IdActor PK }
                LtActorCategory { string IdActorCategory PK }
                TrEntry {
                    string IdEntry PK
                    string IdActorCategory FK
                }
            """);

        schema.Tables["TrEntry"].ForeignKeys.Should().ContainSingle()
            .Which.PrincipalTable.Should().Be("LtActorCategory");
    }

    [Fact]
    public void Generate_TransactionalExport_HasSingleBatchRollbackAndRethrow()
    {
        var script = SqlServerMigrationGenerator.Generate(CreateDiff());

        var batch = SqlServerMigrationGenerator.SplitIntoBatches(script).Should().ContainSingle().Which;
        batch.Should().Contain("SET XACT_ABORT ON;");
        batch.Should().Contain("BEGIN TRY");
        batch.Should().Contain("BEGIN CATCH");
        batch.Should().Contain("IF XACT_STATE() <> 0");
        batch.Should().Contain("ROLLBACK TRANSACTION;");
        batch.Should().Contain("THROW;");
        batch.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal).Should().BeLessThan(batch.IndexOf("BEGIN CATCH", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apply_LiveExecution_LeavesTransactionOwnershipToExecutor()
    {
        var executor = new RecordingExecutor();
        var result = await new SqlServerMigrationApplier(executor).ApplyAsync(CreateDiff(), new SqlServerApplierOptions
        {
            ConnectionString = "Server=masked;Database=masked;Integrated Security=true",
            Transactional = true
        });

        result.Success.Should().BeTrue();
        executor.Transactional.Should().BeTrue();
        executor.Batches.Should().NotBeEmpty();
        executor.Batches.Should().OnlyContain(b => !b.Contains("BEGIN TRANSACTION") && !b.Contains("COMMIT TRANSACTION"));
    }

    private static SchemaDiff CreateDiff()
    {
        var table = new TableSchema { Name = "MaskedEntry" };
        table.AddColumn(new ColumnSchema { Name = "MaskedKey", Type = StandardType.Int, IsPrimaryKey = true });
        var diff = new SchemaDiff();
        diff.Tables.Add(new TableDiff { TableName = table.Name, Kind = DiffKind.Added, Target = table });
        return diff;
    }

    private sealed class RecordingExecutor : ISqlMigrationExecutor
    {
        public bool Transactional { get; private set; }
        public List<string> Batches { get; } = new();

        public Task ExecuteBatchesAsync(string connectionString, IEnumerable<string> sqlBatches, bool transactional, CancellationToken cancellationToken = default)
        {
            Transactional = transactional;
            Batches.AddRange(sqlBatches);
            return Task.CompletedTask;
        }
    }
}
