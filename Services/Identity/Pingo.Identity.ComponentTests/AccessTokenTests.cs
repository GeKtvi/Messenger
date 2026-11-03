using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

// The access token is what other services will trust, so its contents are checked claim by claim.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class AccessTokenTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";

    [Fact]
    public async Task AccessToken_ValidatesOnlyWithTheConfiguredKey()
    {
        var tokens = await Client.RegisterAndLoginAsync(Email);

        (await ValidateAsync(tokens.AccessToken, IdentityApiFactory.SigningKey)).IsValid.Should().BeTrue();
        (await ValidateAsync(tokens.AccessToken, "some-other-signing-key-of-enough-length")).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task AccessToken_HasExactlyTheExpectedClaims()
    {
        var registered = await (await Client.RegisterAsync(Email)).ReadAsync<RegisterResponse>();
        var now = Factory.Time.GetUtcNow();

        var tokens = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();

        var jwt = new JsonWebToken(tokens.AccessToken);
        jwt.Alg.Should().Be("HS256");
        jwt.Typ.Should().Be("JWT");
        jwt.Claims.Select(c => c.Type).Should().BeEquivalentTo("sub", "jti", "iss", "aud", "iat", "nbf", "exp");
        jwt.Subject.Should().Be(registered.UserId.ToString());
        jwt.Issuer.Should().Be("pingo-identity");
        jwt.Audiences.Should().Equal("pingo");
        jwt.IssuedAt.Should().Be(now.UtcDateTime);
        jwt.ValidFrom.Should().Be(now.UtcDateTime);
        jwt.ValidTo.Should().Be(now.AddMinutes(15).UtcDateTime);
    }

    [Fact]
    public async Task AccessToken_ClockWithFractionalSeconds_ExpiresAtMatchesTheExpClaim()
    {
        // "exp" holds whole seconds, so the response must not promise a later, fractional expiry.
        await Client.RegisterAsync(Email);
        Factory.Time.Advance(TimeSpan.FromTicks(1_234_567));

        var tokens = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();

        tokens.AccessTokenExpiresAt.Should().Be(new DateTimeOffset(new JsonWebToken(tokens.AccessToken).ValidTo));
        tokens.AccessTokenExpiresAt.Ticks.Should().Be(
            tokens.AccessTokenExpiresAt.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
    }

    [Fact]
    public async Task AccessToken_TwoLoginsAtTheSameInstant_GetDifferentIds()
    {
        await Client.RegisterAsync(Email);

        var first = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();
        var second = await (await Client.LoginAsync(Email)).ReadAsync<TokenResponse>();

        new JsonWebToken(first.AccessToken).Id.Should().NotBe(new JsonWebToken(second.AccessToken).Id);
    }

    [Fact]
    public async Task AccessToken_AfterRefresh_IsForTheSameUserAndIssuedAtTheRefresh()
    {
        var login = await Client.RegisterAndLoginAsync(Email);
        Factory.Time.Advance(TimeSpan.FromMinutes(20));

        var refreshed = await (await Client.RefreshAsync(login.RefreshToken)).ReadAsync<TokenResponse>();

        var before = new JsonWebToken(login.AccessToken);
        var after = new JsonWebToken(refreshed.AccessToken);
        after.Subject.Should().Be(before.Subject);
        after.Id.Should().NotBe(before.Id);
        after.IssuedAt.Should().Be(Factory.Time.GetUtcNow().UtcDateTime);
    }

    // The token was issued by the fake clock, far from real time, so the lifetime is checked by hand
    // in the tests above instead of here.
    private static Task<TokenValidationResult> ValidateAsync(string token, string signingKey) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "pingo-identity",
            ValidAudience = "pingo",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = false,
        });
}
