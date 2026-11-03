using System.Security.Cryptography;
using System.Text;
using Dapper;
using FluentAssertions;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

// What actually lands in PostgreSQL: secrets only as hashes, and times as the API reported them.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class StorageTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";

    [Fact]
    public async Task Register_StoresTheNormalizedEmailAndASaltedHash()
    {
        await Client.RegisterAsync("  Ann@Example.COM ");

        await using var db = await Factory.OpenConnectionAsync();
        var login = await db.QuerySingleAsync<(string Email, byte[] Hash, byte[] Salt, int Iterations)>(
            "SELECT email, password_hash, password_salt, password_iterations FROM password_logins");
        login.Email.Should().Be(Email);
        login.Hash.Should().HaveCount(32).And.NotEqual(Encoding.UTF8.GetBytes(IdentityHttpClientExtensions.Password));
        login.Salt.Should().HaveCount(16);
        login.Iterations.Should().Be(600_000);
    }

    [Fact]
    public async Task Register_SamePasswordForTwoUsers_GetsDifferentSaltsAndHashes()
    {
        await Client.RegisterAsync("ann@example.com");
        await Client.RegisterAsync("bob@example.com");

        await using var db = await Factory.OpenConnectionAsync();
        var logins = (await db.QueryAsync<(byte[] Hash, byte[] Salt)>(
            "SELECT password_hash, password_salt FROM password_logins")).ToList();
        logins.Should().HaveCount(2);
        logins[0].Salt.Should().NotEqual(logins[1].Salt);
        logins[0].Hash.Should().NotEqual(logins[1].Hash);
    }

    [Fact]
    public async Task Register_TakenEmail_LeavesNoUserWithoutALogin()
    {
        await Client.RegisterAsync(Email);
        await Client.RegisterAsync(Email);

        await using var db = await Factory.OpenConnectionAsync();
        (await db.ExecuteScalarAsync<long>("SELECT count(*) FROM users")).Should().Be(1);
    }

    [Fact]
    public async Task Register_ClockWithSubMicrosecondTicks_ReturnsTheStoredTime()
    {
        // PostgreSQL keeps microseconds and the clock has 100 ns ticks, so the API must round down
        // to what a later read returns.
        Factory.Time.Advance(TimeSpan.FromTicks(1_234_567));
        var now = Factory.Time.GetUtcNow();
        var expected = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));

        var registered = await (await Client.RegisterAsync(Email)).ReadAsync<RegisterResponse>();

        registered.RegisteredAt.Should().Be(expected);
        await using var db = await Factory.OpenConnectionAsync();
        var stored = await db.ExecuteScalarAsync<DateTime>("SELECT registered_at FROM users");
        new DateTimeOffset(stored).Should().Be(expected);
    }

    [Fact]
    public async Task Refresh_ClockWithSubMicrosecondTicks_KeepsTheSessionExpiryExactly()
    {
        await Client.RegisterAsync(Email);
        Factory.Time.Advance(TimeSpan.FromTicks(1_234_567));
        var login = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();

        var refreshed = await (await Client.RefreshAsync(login.RefreshToken)).ReadAsync<TokenResponse>();

        refreshed.RefreshTokenExpiresAt.Should().Be(login.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Refresh_StoresOnlyTheTokenHash_AndReplacesTheRow()
    {
        var registered = await (await Client.RegisterAsync(Email)).ReadAsync<RegisterResponse>();
        var login = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();
        await using var db = await Factory.OpenConnectionAsync();
        const string Sql = "SELECT token_hash, user_id, created_at, expires_at FROM refresh_tokens";

        var first = await db.QuerySingleAsync<(byte[] Hash, Guid UserId, DateTime CreatedAt, DateTime ExpiresAt)>(Sql);
        first.Hash.Should().Equal(Sha256(login.RefreshToken));
        first.UserId.Should().Be(registered.UserId);

        Factory.Time.Advance(TimeSpan.FromHours(1));
        var refreshed = await (await Client.RefreshAsync(login.RefreshToken)).ReadAsync<TokenResponse>();

        var second = await db.QuerySingleAsync<(byte[] Hash, Guid UserId, DateTime CreatedAt, DateTime ExpiresAt)>(Sql);
        second.Hash.Should().Equal(Sha256(refreshed.RefreshToken));
        second.UserId.Should().Be(registered.UserId);
        new DateTimeOffset(second.CreatedAt).Should().Be(Factory.Time.GetUtcNow());
        second.ExpiresAt.Should().Be(first.ExpiresAt);
    }

    private static byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
