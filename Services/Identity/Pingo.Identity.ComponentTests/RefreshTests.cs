using System.Net;
using FluentAssertions;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class RefreshTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";
    private const string InvalidTokenTitle = "The refresh token is invalid or has expired.";

    [Fact]
    public async Task Refresh_ValidToken_ReturnsNewTokensWithTheSameSessionExpiry()
    {
        var login = await Client.RegisterAndLoginAsync(Email);
        Factory.Time.Advance(TimeSpan.FromMinutes(20));

        var response = await Client.RefreshAsync(login.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await response.ReadAsync<TokenResponse>();
        refreshed.AccessToken.Should().NotBe(login.AccessToken);
        refreshed.AccessTokenExpiresAt.Should().Be(Factory.Time.GetUtcNow().AddMinutes(15));
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
        refreshed.RefreshTokenExpiresAt.Should().Be(login.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Refresh_RotatedToken_CanBeUsedOnce()
    {
        var login = await Client.RegisterAndLoginAsync(Email);
        var refreshed = await (await Client.RefreshAsync(login.RefreshToken)).ReadAsync<TokenResponse>();

        var response = await Client.RefreshAsync(refreshed.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_AlreadyUsedToken_Returns401InvalidRefreshToken()
    {
        var login = await Client.RegisterAndLoginAsync(Email);
        (await Client.RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.RefreshAsync(login.RefreshToken);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid-refresh-token", InvalidTokenTitle);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Returns401InvalidRefreshToken()
    {
        var login = await Client.RegisterAndLoginAsync(Email);
        Factory.Time.Advance(TimeSpan.FromDays(30));

        var response = await Client.RefreshAsync(login.RefreshToken);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid-refresh-token", InvalidTokenTitle);
    }

    [Fact]
    public async Task Refresh_JustBeforeTheSessionEnds_Succeeds()
    {
        var login = await Client.RegisterAndLoginAsync(Email);
        Factory.Time.Advance(TimeSpan.FromDays(30) - TimeSpan.FromMicroseconds(1));

        var response = await Client.RefreshAsync(login.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_RotatedTokenAfterTheSessionEnds_Returns401InvalidRefreshToken()
    {
        // The rotated token keeps the session's end, so refreshing on day 29 doesn't extend it.
        var login = await Client.RegisterAndLoginAsync(Email);
        Factory.Time.Advance(TimeSpan.FromDays(29));
        var refreshed = await (await Client.RefreshAsync(login.RefreshToken)).ReadAsync<TokenResponse>();
        Factory.Time.Advance(TimeSpan.FromDays(1));

        var response = await Client.RefreshAsync(refreshed.RefreshToken);

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid-refresh-token", InvalidTokenTitle);
    }

    [Fact]
    public async Task Refresh_OneOfTwoSessions_LeavesTheOtherWorking()
    {
        var phone = await Client.RegisterAndLoginAsync(Email);
        var laptop = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();

        (await Client.RefreshAsync(phone.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await Client.RefreshAsync(laptop.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401InvalidRefreshToken()
    {
        var response = await Client.RefreshAsync("not-a-real-token");

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "invalid-refresh-token", InvalidTokenTitle);
    }
}
