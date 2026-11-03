using System.Net;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingo.Identity.Data;

namespace Pingo.Identity.ComponentTests;

// Simultaneous requests for the same token or email: PostgreSQL, not timing, must decide the winner.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class ConcurrencyTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const int Requests = 10;

    [Fact]
    public async Task Refresh_SameTokenManyTimesAtOnce_SucceedsExactlyOnce()
    {
        var login = await Client.RegisterAndLoginAsync("ann@example.com");

        // A second app on the same database, whose refreshes all find the token before any replaces it.
        await using var gated = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRefreshTokenRepository>();
            services.AddSingleton<IRefreshTokenRepository>(provider => new FindGateRefreshTokenRepository(
                new RefreshTokenRepository(provider.GetRequiredService<IdentityDatabase>()), Requests));
        }));
        var client = gated.CreateClient();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, Requests).Select(_ => client.RefreshAsync(login.RefreshToken)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
        {
            await rejected.ShouldBeProblemAsync(
                HttpStatusCode.Unauthorized, "invalid-refresh-token", "The refresh token is invalid or has expired.");
        }

        await using var db = await Factory.OpenConnectionAsync();
        (await db.ExecuteScalarAsync<long>("SELECT count(*) FROM refresh_tokens")).Should().Be(1);
    }

    [Fact]
    public async Task Register_SameEmailManyTimesAtOnce_CreatesOneUser()
    {
        // Different spellings of one email, so normalization happens before the unique check.
        var responses = await Task.WhenAll(Enumerable.Range(0, Requests)
            .Select(i => Client.RegisterAsync(i % 2 == 0 ? "ann@example.com" : " ANN@Example.com")));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        foreach (var rejected in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await rejected.ShouldBeProblemAsync(
                HttpStatusCode.Conflict, "email-taken", "A user with this email already exists.");
        }

        await using var db = await Factory.OpenConnectionAsync();
        (await db.ExecuteScalarAsync<long>("SELECT count(*) FROM users")).Should().Be(1);
        (await db.ExecuteScalarAsync<long>("SELECT count(*) FROM password_logins")).Should().Be(1);
    }
}
