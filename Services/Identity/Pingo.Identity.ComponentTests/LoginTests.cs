using System.Net;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class LoginTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";

    [Fact]
    public async Task Login_CorrectPassword_ReturnsTokensForTheUser()
    {
        var registered = await (await Client.RegisterAsync(Email)).ReadAsync<RegisterResponse>();

        var response = await Client.LoginAsync(Email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await response.ReadSignedInAsync();
        var now = Factory.Time.GetUtcNow();
        tokens.TokenType.Should().Be("Bearer");
        tokens.AccessTokenExpiresAt.Should().Be(now.AddMinutes(15));
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshTokenExpiresAt.Should().Be(now.AddDays(30));
        new JsonWebToken(tokens.AccessToken).Subject.Should().Be(registered.UserId.ToString());
    }

    [Fact]
    public async Task Login_EmailInOtherCase_Succeeds()
    {
        await Client.RegisterAsync(Email);

        var response = await Client.LoginAsync("Ann@Example.COM");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401UserNotFound()
    {
        var response = await Client.LoginAsync(Email);

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "user-not-found", "No user with this email was found.");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401WrongPassword()
    {
        await Client.RegisterAsync(Email);

        var response = await Client.LoginAsync(Email, "wrong password!");

        await response.ShouldBeProblemAsync(HttpStatusCode.Unauthorized, "wrong-password", "Wrong password.");
    }
}
