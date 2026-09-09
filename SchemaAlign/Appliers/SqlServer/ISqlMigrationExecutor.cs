using Microsoft.Data.SqlClient;

namespace SchemaAlign.Appliers.SqlServer;

/// <summary>
/// Abstraction for executing SQL migration script batches against a target SQL Server database.
/// </summary>
public interface ISqlMigrationExecutor
{
    /// <summary>
    /// Executes a sequence of SQL batches against the specified connection string.
    /// </summary>
    Task ExecuteBatchesAsync(
        string connectionString,
        IEnumerable<string> sqlBatches,
        bool transactional,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of <see cref="ISqlMigrationExecutor"/> using <see cref="SqlConnection"/>.
/// </summary>
public class SqlMigrationExecutor : ISqlMigrationExecutor
{
    public async Task ExecuteBatchesAsync(
        string connectionString,
        IEnumerable<string> sqlBatches,
        bool transactional,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        SqlTransaction? transaction = null;
        if (transactional)
        {
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            foreach (var batch in sqlBatches)
            {
                var cleanBatch = batch.Trim();
                if (string.IsNullOrWhiteSpace(cleanBatch))
                {
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.CommandText = cleanBatch;
                if (transaction != null)
                {
                    command.Transaction = transaction;
                }

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction != null)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Ignore rollback errors if connection was lost
                }
            }
            throw;
        }
    }
}
