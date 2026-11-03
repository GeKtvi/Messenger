using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Pingo.Messages.ComponentTests;

// The frontend runs on another origin, so without the right CORS headers the browser blocks
// every call and the chat silently stays empty.
public class MessagesCorsTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    // The origins from the API's appsettings.json, so the test follows the real configuration.
    private string AllowedOrigin =>
        factory.Services.GetRequiredService<IConfiguration>()
            .GetSection("Cors:AllowedOrigins").Get<string[]>()!.First();

    [Fact]
    public async Task Preflight_FromAllowedOrigin_AllowsThePut()
    {
        var response = await _client.SendAsync(Preflight(AllowedOrigin));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Methods").Should().Contain("PUT");
        response.Headers.GetValues("Access-Control-Allow-Headers").Should().Contain(h => h.Contains("authorization"));
    }

    [Fact]
    public async Task Preflight_WithoutAToken_IsNotRejected()
    {
        // Browsers never send the Authorization header on a preflight, so it must pass without one.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.SendAsync(Preflight(AllowedOrigin));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Unauthorized_FromAllowedOrigin_StillHasAllowOriginHeader()
    {
        // Without it, the browser hides the 401 from the frontend, which then can't refresh its token.
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/messages");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(AllowedOrigin);
    }

    [Fact]
    public async Task Get_FromAllowedOrigin_HasAllowOriginHeader()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/messages");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(AllowedOrigin);
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_HasNoCorsHeaders()
    {
        var response = await _client.SendAsync(Preflight("https://evil.example"));

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, $"/api/messages/{Guid.NewGuid()}");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }
}
