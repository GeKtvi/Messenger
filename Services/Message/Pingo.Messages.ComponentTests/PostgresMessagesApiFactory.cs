using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Pingo.Messages.ComponentTests;

// Runs the service against a real PostgreSQL in a throwaway Docker container (Testcontainers).
// Slower and needs Docker, but exercises real constraints, PostgreSQL errors and the migrations,
// which the app applies on startup exactly as in production.
public class PostgresMessagesApiFactory : MessagesApiFactoryBase, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    // When set, holds every message insert until all expected requests reach it (see InsertGate).
    public InsertGate? InsertGate { get; set; }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureDatabase(DbContextOptionsBuilder options) =>
        options
            .UseNpgsql(_postgres.GetConnectionString())
            .AddInterceptors(new InsertGateInterceptor(() => InsertGate));

    // Deletes rows but keeps the schema, so the migrations applied at startup stay in place.
    public override async Task ResetDatabaseAsync()
    {
        InsertGate = null;
        using var scope = Services.CreateScope();
        await CreateDbContext(scope).Messages.ExecuteDeleteAsync();
    }
}
