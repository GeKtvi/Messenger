using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Pingo.Identity.Data;

// Creates the database if needed and applies pending migrations when the app starts.
// If this fails, the exception stops the host (BackgroundServiceExceptionBehavior.StopHost).
internal sealed class DatabaseMigrator(IdentityDatabase database, ILogger<DatabaseMigrator> logger) : BackgroundService
{
    // Any fixed number: only one app instance at a time can hold this lock and migrate.
    private const long MigrationLockId = 7_301_202_603;

    private const string CreateHistoryTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version    integer     PRIMARY KEY,
            name       text        NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT now()
        );
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await EnsureDatabaseExistsAsync(stoppingToken);
            await ApplyMigrationsAsync(stoppingToken);
            database.MarkReady();
        }
        catch (Exception ex)
        {
            database.MarkFailed(ex);
            throw;
        }
    }

    // Local development convenience: the shared PostgreSQL container only creates the Message
    // service's database, so the Identity database is created here on first start.
    private async Task EnsureDatabaseExistsAsync(CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        var name = builder.Database ?? throw new InvalidOperationException("The Identity connection string has no database.");
        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", new { name }, cancellationToken: ct));
        if (exists)
            return;

        try
        {
            // A database name can't be a query parameter, so it's quoted as an identifier instead.
            var quotedName = "\"" + name.Replace("\"", "\"\"") + "\"";
            await connection.ExecuteAsync(new CommandDefinition($"CREATE DATABASE {quotedName}", cancellationToken: ct));
            logger.LogInformation("Created database {Database}", name);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // Another instance created it at the same moment.
        }
    }

    private async Task ApplyMigrationsAsync(CancellationToken ct)
    {
        await using var connection = await database.DataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_lock(@MigrationLockId)", new { MigrationLockId }, cancellationToken: ct));
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(CreateHistoryTableSql, cancellationToken: ct));
            var applied = (await connection.QueryAsync<int>(new CommandDefinition(
                "SELECT version FROM schema_migrations", cancellationToken: ct))).ToHashSet();

            foreach (var migration in Migrations.All.Where(m => !applied.Contains(m.Version)).OrderBy(m => m.Version))
            {
                // A migration and its history row commit together, so a failure leaves nothing half-applied.
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await connection.ExecuteAsync(new CommandDefinition(migration.Sql, transaction: transaction, cancellationToken: ct));
                await connection.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO schema_migrations (version, name) VALUES (@Version, @Name)",
                    migration, transaction, cancellationToken: ct));
                await transaction.CommitAsync(ct);

                logger.LogInformation("Applied migration {Version} {Name}", migration.Version, migration.Name);
            }
        }
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@MigrationLockId)", new { MigrationLockId });
        }
    }
}
