using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace Pingo.Messages.Data;

// Repeats a query or save that failed for a reason that usually passes, such as a pooled connection that
// died when PostgreSQL restarted. Safe here: every write is idempotent (D-023), and there are no explicit
// transactions, which a retrying strategy doesn't allow.
internal sealed class RetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, NpgsqlDataSource dataSource)
    : NpgsqlRetryingExecutionStrategy(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(1), errorCodesToAdd: null)
{
    protected override void OnRetry()
    {
        // With several hosts (D-070), the failure marked the server as down for a few seconds, so the retry
        // would find no server at all. It's most likely back, so check again.
        if (dataSource is NpgsqlMultiHostDataSource multiHost)
            multiHost.ClearDatabaseStates();
        base.OnRetry();
    }
}
