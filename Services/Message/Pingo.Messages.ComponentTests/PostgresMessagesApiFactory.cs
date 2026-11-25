using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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

    private NpgsqlDataSource? _dataSource;

    public Task InitializeAsync() => _postgres.StartAsync();

    // What the app connects with. Tests can list the server more than once, as the lab lists every
    // server that may be the primary (D-070).
    protected virtual string ConnectionString => _postgres.GetConnectionString();

    public string ContainerConnectionString => _postgres.GetConnectionString();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureDatabase(DbContextOptionsBuilder options) =>
        MessagesServiceCollectionExtensions
            .UseDatabase(options, _dataSource ??= NpgsqlDataSource.Create(ConnectionString))
            .AddInterceptors(new InsertGateInterceptor(() => InsertGate));

    // Deletes rows but keeps the schema, so the migrations applied at startup stay in place.
    public override async Task ResetDatabaseAsync()
    {
        InsertGate = null;
        using var scope = Services.CreateScope();
        await CreateDbContext(scope).Messages.ExecuteDeleteAsync();
    }
}
