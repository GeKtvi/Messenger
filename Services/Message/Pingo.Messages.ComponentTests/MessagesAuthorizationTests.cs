using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pingo.Messages.ComponentTests;

// Only requests with a valid access token from the Identity service reach the messages.
public class MessagesAuthorizationTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>
{
    [Fact]
    public async Task ValidToken_CanListAndSend()
    {
        using var client = ClientWith(TestAccessTokens.Create());

        (await client.GetAsync("/api/messages")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendMessageAsync(Guid.NewGuid(), "hello")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task NoToken_Returns401ForEveryEndpoint()
    {
        using var client = ClientWith(token: null);

        (await client.GetAsync("/api/messages")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.SendMessageAsync(Guid.NewGuid(), "hello")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NoToken_DoesNotSend()
    {
        var id = Guid.NewGuid();
        using var anonymous = ClientWith(token: null);
        await anonymous.SendMessageAsync(id, "sneaky");

        using var signedIn = ClientWith(TestAccessTokens.Create());
        (await signedIn.GetMessagesAsync()).Should().NotContain(m => m.Id == id);
    }

    [Fact]
    public async Task Unauthorized_TellsTheClientToUseABearerToken()
    {
        using var client = ClientWith(token: null);

        var response = await client.GetAsync("/api/messages");

        response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
    }

    public static TheoryData<string, string> RejectedTokens() => new()
    {
        { "expired", TestAccessTokens.Create(expiresAt: DateTime.UtcNow.AddMinutes(-1)) },
        { "signed with another key", TestAccessTokens.Create(signingKey: "some-other-signing-key-of-enough-length") },
        { "from another issuer", TestAccessTokens.Create(issuer: "someone-else") },
        { "for another audience", TestAccessTokens.Create(audience: "another-app") },
        { "unsigned (alg none)", Unsigned() },
        { "not a JWT", "not-a-jwt" },
    };

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task InvalidToken_Returns401(string because, string token)
    {
        using var client = ClientWith(token);

        var response = await client.GetAsync("/api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, because);
    }

    [Fact]
    public async Task TokenExpiredWithinTheClockSkew_IsStillAccepted()
    {
        // 30 seconds of slack for clock drift between Identity and this service.
        using var client = ClientWith(TestAccessTokens.Create(expiresAt: DateTime.UtcNow.AddSeconds(-10)));

        (await client.GetAsync("/api/messages")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient ClientWith(string? token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // A token with valid claims but no signature at all.
    private static string Unsigned() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestAccessTokens.Issuer,
            Audience = TestAccessTokens.Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString() },
        });
}
