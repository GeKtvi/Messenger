using System.Net;
using System.Net.Http.Headers;

namespace Pingo.Frontend.Services.Auth;

// Added to the Message API's HttpClient, so pages and services never deal with tokens:
// - every request carries "Authorization: Bearer <access token>";
// - on 401 (usually an expired access token) it refreshes the token through the cookie and sends
//   the request once more with the new token. If the session has ended, the 401 is returned and
//   the session is signed out, which brings the login form back.
public class AccessTokenHandler(IAuthSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = session.AccessToken;
        SetToken(request, token);
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null)
            return response;

        // Another request may have refreshed already while this one was on its way.
        if (session.AccessToken == token)
            await session.RefreshAsync();

        if (session.AccessToken is not { } newToken || newToken == token)
            return response;

        // A stream body may already be consumed by the first send, and buffering it to resend would
        // hold a whole upload in memory. The token is refreshed anyway, so the caller's next attempt
        // works. JSON, string and byte bodies can be sent again as they are.
        if (request.Content is StreamContent)
            return response;

        // Only one retry: a 401 with a fresh token isn't something another refresh would fix.
        // Resending the same request is fine here: only HttpClient.SendAsync refuses a second send.
        response.Dispose();
        SetToken(request, newToken);
        return await base.SendAsync(request, ct);
    }

    private static void SetToken(HttpRequestMessage request, string? token) =>
        request.Headers.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
}
