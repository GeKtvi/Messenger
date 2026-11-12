using System.Net.Http.Headers;

namespace Pingo.Frontend.Services.Auth;

// Added to the Message API's HttpClient: every request carries "Authorization: Bearer <access token>",
// so pages and services never deal with the token themselves.
public class AccessTokenHandler(AuthSession session) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (session.AccessToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, ct);
    }
}
