using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Pingo.Frontend.Services.Auth;

public record TokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, DateTimeOffset RefreshTokenExpiresAt);

// The outcome of a login or registration: tokens on success, or the server's (localized) message.
public record AuthOutcome(TokenResponse? Tokens, string? Error)
{
    public bool Succeeded => Tokens is not null;
}

// Calls the Identity service. Every request includes credentials, so the browser sends and stores
// the HttpOnly refresh token cookie; this code never sees the refresh token itself.
public class IdentityApiClient(HttpClient http)
{
    public async Task<AuthOutcome> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/login", new { email, password }, ct);
        return response.IsSuccessStatusCode
            ? new AuthOutcome(await response.Content.ReadFromJsonAsync<TokenResponse>(ct), null)
            : new AuthOutcome(null, await ReadErrorAsync(response, ct));
    }

    // Returns null on success, or the server's message.
    public async Task<string?> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/register", new { email, password }, ct);
        return response.IsSuccessStatusCode ? null : await ReadErrorAsync(response, ct);
    }

    // Uses the refresh token cookie; null if there's no valid session, which only a 401 says. Any other
    // failure throws HttpRequestException: the session may well still be valid.
    public async Task<TokenResponse?> RefreshAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/refresh", body: null, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/auth/logout", body: null, ct);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, uri);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return http.SendAsync(request, ct);
    }

    // Problem Details: "title" is already in the browser's language. A validation problem lists the
    // field messages in "errors" instead.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
            var fieldErrors = problem?.Errors?.SelectMany(e => e.Value).ToList();
            if (fieldErrors is { Count: > 0 })
                return string.Join(" ", fieldErrors);
            if (!string.IsNullOrWhiteSpace(problem?.Title))
                return problem.Title;
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return $"Request failed ({(int)response.StatusCode}).";
    }

    private record Problem(string? Title, Dictionary<string, string[]>? Errors);
}
