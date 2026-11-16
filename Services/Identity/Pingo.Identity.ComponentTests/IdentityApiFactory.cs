using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using OpenTelemetry.Trace;
using Pingo.Identity.Data;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Pingo.Identity.ComponentTests;

// Starts the real Identity service in memory against PostgreSQL and Redis (Hangfire's job storage)
// in throwaway Docker containers (Testcontainers). The containers start once for all test classes
// (see IdentityCollection) and are removed when they finish. Respawn empties the tables after each test, so tests don't see each
// other's data while the app and its schema stay up.
public sealed class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("pingo_identity")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:8").Build();

    // The JWT signing key the app runs with, so tests can check token signatures.
    public const string SigningKey = "pingo-component-tests-signing-key-not-a-secret";

    private Respawner _respawner = null!;

    // Every span the app finishes, recorded in memory instead of sent to Jaeger.
    public RecordedSpans Spans { get; } = new();

    // Tests control "now" through this clock instead of the system clock.
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Starts the app. Its BackgroundService applies the migrations, and IdentityDatabase hands
        // out connections only after they finish, so Respawn below sees the full schema.
        await using (await Services.GetRequiredService<IdentityDatabase>().OpenConnectionAsync())
        {
        }

        await using var connection = await OpenConnectionAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // The applied-migrations list is schema, not data: clearing it would make the next
            // start try to apply every migration again.
            TablesToIgnore = [new Table("public", "schema_migrations")],
        });
    }

    // Deletes all rows, in foreign-key order, from every table except schema_migrations.
    public async Task ResetDatabaseAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await _respawner.ResetAsync(connection);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Identity", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        // Never connected to: the test harness below replaces RabbitMQ.
        builder.UseSetting("ConnectionStrings:RabbitMq", "amqp://unused-in-tests/");
        // appsettings.Development.json isn't loaded in Testing, so the key comes from here.
        builder.UseSetting("Jwt:SigningKey", SigningKey);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            // MassTransit's in-memory test harness instead of RabbitMQ, so tests can see what was published.
            services.AddMassTransitTestHarness(harness =>
                harness.SetTestTimeouts(testInactivityTimeout: TimeSpan.FromSeconds(1)));

            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(Spans));
        });
    }

    // A direct connection to the test database, for checking what the app stored.
    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }
}
