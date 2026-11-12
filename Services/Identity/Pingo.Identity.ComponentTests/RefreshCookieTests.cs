using System.Net;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace Pingo.Identity.ComponentTests;

// The refresh token only ever travels in an HttpOnly cookie, so page scripts can't read it.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class RefreshCookieTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";
    private const string FrontendOrigin = "http://localhost:5221";

    [Fact]
    public async Task Login_SetsTheRefreshTokenAsAProtectedCookie()
    {
        await Client.RegisterAsync(Email);

        var response = await Client.LoginAsync(Email);

        var cookie = response.ReadRefreshCookie()!;
        var signedIn = await response.ReadSignedInAsync();
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.Strict);
        cookie.Path.Value.Should().Be("/api/auth");
        // Cookie dates are whole seconds.
        cookie.Expires.Should().Be(signedIn.RefreshTokenExpiresAt.AddTicks(
            -(signedIn.RefreshTokenExpiresAt.Ticks % TimeSpan.TicksPerSecond)));
    }

    [Fact]
    public async Task Login_DoesNotPutTheRefreshTokenInTheBody()
    {
        await Client.RegisterAsync(Email);

        var response = await Client.LoginAsync(Email);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(response.ReadRefreshCookie()!.Value.Value).And.NotContain("\"refreshToken\"");
    }

    [Fact]
    public async Task Refresh_WithoutACookie_Returns401()
    {
        var response = await Client.RefreshAsync(refreshToken: null);

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Unauthorized, "invalid-refresh-token", "The refresh token is invalid or has expired.");
    }

    [Fact]
    public async Task Refresh_InvalidCookie_DeletesIt()
    {
        var response = await Client.RefreshAsync("not-a-real-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.ReadRefreshCookie()!.Expires.Should().BeBefore(DateTimeOffset.UtcNow, "an expired cookie tells the browser to drop it");
    }

    [Fact]
    public async Task Refresh_OverlongCookie_Returns401()
    {
        var response = await Client.RefreshAsync(new string('t', 257));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_EndsTheSessionAndDeletesTheCookie()
    {
        var login = await Client.RegisterAndLoginAsync(Email);

        var response = await Client.LogoutAsync(login.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.ReadRefreshCookie()!.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
        (await Client.RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var db = await Factory.OpenConnectionAsync();
        (await db.ExecuteScalarAsync<long>("SELECT count(*) FROM refresh_tokens")).Should().Be(0);
    }

    [Fact]
    public async Task Logout_OneSession_LeavesTheOtherWorking()
    {
        var phone = await Client.RegisterAndLoginAsync(Email);
        var laptop = await (await Client.LoginAsync(Email)).ReadSignedInAsync();

        await Client.LogoutAsync(phone.RefreshToken);

        (await Client.RefreshAsync(laptop.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithoutASession_Returns204()
    {
        var response = await Client.LogoutAsync(refreshToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task BrowserLikeClient_KeepsTheSessionThroughTheCookieAlone()
    {
        // A client with a cookie jar over HTTPS, like the browser: it never handles the token itself.
        // The jar checks the cookie's expiry against the real clock and drops it if it's already past,
        // so the app's clock has to catch up with real time first.
        var realNow = DateTimeOffset.UtcNow;
        if (Factory.Time.GetUtcNow() < realNow)
            Factory.Time.SetUtcNow(realNow);
        using var browser = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });
        await browser.RegisterAsync(Email);
        (await browser.LoginAsync(Email)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await browser.PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await browser.PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preflight_FromTheFrontend_AllowsCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", FrontendOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(FrontendOrigin);
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().Equal("true");
    }

    [Fact]
    public async Task Preflight_FromAnUnknownOrigin_GetsNoCorsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await Client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
