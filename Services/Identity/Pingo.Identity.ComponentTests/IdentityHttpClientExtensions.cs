using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

public static class IdentityHttpClientExtensions
{
    public const string Password = "correct horse battery";

    public static Task<HttpResponseMessage> RegisterAsync(
        this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = password });

    public static Task<HttpResponseMessage> LoginAsync(
        this HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest { RefreshToken = refreshToken });

    // Registers the user and logs in, for tests that start from a signed-in session.
    public static async Task<TokenResponse> RegisterAndLoginAsync(this HttpClient client, string email)
    {
        (await client.RegisterAsync(email)).StatusCode.Should().Be(HttpStatusCode.Created);
        var login = await client.LoginAsync(email);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return await login.ReadAsync<TokenResponse>();
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
