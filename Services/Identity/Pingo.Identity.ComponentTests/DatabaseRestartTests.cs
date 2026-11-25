using System.Net;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Npgsql;

namespace Pingo.Identity.ComponentTests;

// After PostgreSQL restarts, the app's idle pooled connections are dead, but the app doesn't know
// until it uses one. pg_terminate_backend ends them the same way, without restarting the container.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class DatabaseRestartTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    [Fact]
    public async Task Login_AfterTheServerEndedEveryConnection_Succeeds()
    {
        await Client.RegisterAsync("ann@example.com");
        await FillThePoolAsync();

        await TerminateAppConnectionsAsync();

        (await Client.LoginAsync("ann@example.com")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_AfterTheServerEndedEveryConnection_Succeeds()
    {
        var signedIn = await Client.RegisterAndLoginAsync("ann@example.com");
        await FillThePoolAsync();

        await TerminateAppConnectionsAsync();

        (await Client.RefreshAsync(signedIn.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_AfterTheServerEndedEveryConnection_Succeeds()
    {
        await FillThePoolAsync();

        await TerminateAppConnectionsAsync();

        (await Client.RegisterAsync("ann@example.com")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Login_WithSeveralHostsListed_AfterTheServerEndedEveryConnection_Succeeds()
    {
        // As in the lab, which lists every server that may be the primary (D-070). The failure marks the
        // server as down, and the retry must not believe that.
        var connectionString = new NpgsqlConnectionStringBuilder(Factory.ConnectionString);
        connectionString.Host = $"{connectionString.Host}:{connectionString.Port},{connectionString.Host}:{connectionString.Port}";
        connectionString.TargetSessionAttributes = "primary";
        await using var app = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Identity", connectionString.ConnectionString));
        var client = app.CreateClient(ClientOptions);
        await client.RegisterAsync("ann@example.com");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.LoginAsync("ann@example.com")));

        await TerminateAppConnectionsAsync();

        (await client.LoginAsync("ann@example.com")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Several requests at once, so the pool holds several idle connections afterwards.
    private async Task FillThePoolAsync()
    {
        await Client.RegisterAsync("pool@example.com");
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Client.LoginAsync("pool@example.com")));
    }

    // Ends every connection to the database except this one.
    private async Task TerminateAppConnectionsAsync()
    {
        await using var connection = await Factory.OpenConnectionAsync();
        var ended = await connection.ExecuteScalarAsync<int>("""
            SELECT count(pg_terminate_backend(pid))
            FROM pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
            """);
        ended.Should().BePositive("the app has idle connections in its pool");
    }
}
