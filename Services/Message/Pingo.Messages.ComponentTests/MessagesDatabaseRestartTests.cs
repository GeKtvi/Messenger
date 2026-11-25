using System.Net;
using FluentAssertions;
using Npgsql;

namespace Pingo.Messages.ComponentTests;

// After PostgreSQL restarts, the app's idle pooled connections are dead, but the app doesn't know
// until it uses one. pg_terminate_backend ends them the same way, without restarting the container.
// Needs Docker. Skip with: dotnet test --filter "Category!=Docker"
[Trait("Category", "Docker")]
public class MessagesDatabaseRestartTests(PostgresMessagesApiFactory factory)
    : DatabaseRestartTestsBase(factory), IClassFixture<PostgresMessagesApiFactory>;

// The same with both hosts listed, as in the lab: the failure marks the server as down, and the retry
// must not believe that.
[Trait("Category", "Docker")]
public class MessagesDatabaseRestartWithSeveralHostsTests(SeveralHostsPostgresMessagesApiFactory factory)
    : DatabaseRestartTestsBase(factory), IClassFixture<SeveralHostsPostgresMessagesApiFactory>;

public class SeveralHostsPostgresMessagesApiFactory : PostgresMessagesApiFactory
{
    protected override string ConnectionString
    {
        get
        {
            var builder = new NpgsqlConnectionStringBuilder(ContainerConnectionString);
            builder.Host = $"{builder.Host}:{builder.Port},{builder.Host}:{builder.Port}";
            builder.TargetSessionAttributes = "primary";
            return builder.ConnectionString;
        }
    }
}

public abstract class DatabaseRestartTestsBase(PostgresMessagesApiFactory factory) : IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_AfterTheServerEndedEveryConnection_Succeeds()
    {
        await FillThePoolAsync();

        await TerminateAppConnectionsAsync();

        (await _client.GetAsync("/api/messages")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Send_AfterTheServerEndedEveryConnection_Succeeds()
    {
        await FillThePoolAsync();

        await TerminateAppConnectionsAsync();

        (await _client.SendMessageAsync(Guid.NewGuid(), "hello")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // Several requests at once, so the pool holds several idle connections afterwards.
    private Task FillThePoolAsync() =>
        Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _client.SendMessageAsync(Guid.NewGuid(), "warm-up")));

    // Ends every connection to the database except the one asking.
    private async Task TerminateAppConnectionsAsync()
    {
        // Not pooled, so it isn't one of the connections being ended.
        await using var connection = new NpgsqlConnection(factory.ContainerConnectionString + ";Pooling=false");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(pg_terminate_backend(pid))::int
            FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
            """, connection);
        var ended = (int)(await command.ExecuteScalarAsync())!;
        ended.Should().BePositive("the app has idle connections in its pool");
    }
}
