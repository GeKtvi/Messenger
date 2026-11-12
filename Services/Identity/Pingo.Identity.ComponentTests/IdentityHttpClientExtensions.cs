using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Pingo.Identity.Api.Auth;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

// A login or refresh as the client sees it: the body plus the refresh token from its cookie.
public record SignedIn(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string TokenType);

public static class IdentityHttpClientExtensions
{
    public const string Password = "correct horse battery";

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = password });

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });

    // The test clients don't keep cookies, so each call says which refresh token cookie to send:
    // tests can then hold several tokens at once, like an old one and its replacement.
    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string? refreshToken) =>
        client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/refresh", refreshToken));

    public static Task<HttpResponseMessage> LogoutAsync(this HttpClient client, string? refreshToken) =>
        client.SendAsync(WithRefreshCookie(HttpMethod.Post, "/api/auth/logout", refreshToken));

    public static HttpRequestMessage WithRefreshCookie(HttpMethod method, string uri, string? refreshToken)
    {
        var request = new HttpRequestMessage(method, uri);
        if (refreshToken is not null)
            request.Headers.Add("Cookie", $"{RefreshTokenCookie.Name}={refreshToken}");
        return request;
    }

    // The refresh token cookie a response sets, or null if it sets none.
    public static SetCookieHeaderValue? ReadRefreshCookie(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? SetCookieHeaderValue.ParseList(values.ToList()).SingleOrDefault(c => c.Name == RefreshTokenCookie.Name)
            : null;

    // Reads a successful login or refresh: the body, and the refresh token from the cookie.
    public static async Task<SignedIn> ReadSignedInAsync(this HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.ReadAsync<TokenResponse>();
        var cookie = response.ReadRefreshCookie();
        cookie.Should().NotBeNull("a login or refresh sets the refresh token cookie");
        return new SignedIn(
            body.AccessToken, body.AccessTokenExpiresAt, cookie!.Value.Value!, body.RefreshTokenExpiresAt, body.TokenType);
    }

    // Registers the user and logs in, for tests that start from a signed-in session.
    public static async Task<SignedIn> RegisterAndLoginAsync(this HttpClient client, string email)
    {
        (await client.RegisterAsync(email)).StatusCode.Should().Be(HttpStatusCode.Created);
        return await (await client.LoginAsync(email)).ReadSignedInAsync();
    }

    // Checks the response is a 400 validation problem and returns its errors by field.
    public static async Task<IDictionary<string, string[]>> ReadValidationErrorsAsync(this HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return (await response.ReadAsync<HttpValidationProblemDetails>()).Errors;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;

    // Checks the response is one of our problems: status, media type, the "type" URN and its title.
    public static async Task ShouldBeProblemAsync(
        this HttpResponseMessage response, HttpStatusCode status, string code, string title)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.ReadAsync<ProblemDetails>();
        problem.Type.Should().Be($"urn:pingo:problem:{code}");
        problem.Title.Should().Be(title);
        problem.Status.Should().Be((int)status);
    }
}
