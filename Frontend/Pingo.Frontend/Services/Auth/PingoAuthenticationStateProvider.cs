using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace Pingo.Frontend.Services.Auth;

// Tells Blazor's <AuthorizeView> who is signed in. The first call waits for the session to be
// restored from the refresh token cookie, so the login screen doesn't flash on reload.
public class PingoAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly IAuthSession _session;

    public PingoAuthenticationStateProvider(IAuthSession session)
    {
        _session = session;
        _session.Changed += OnSessionChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _session.RestoreAsync();
        return new AuthenticationState(ToPrincipal(_session.AccessToken));
    }

    private void OnSessionChanged() =>
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(ToPrincipal(_session.AccessToken))));

    // Reads the user id from the token for display only. The token's signature is checked by the
    // services, not here: the browser holds no key.
    private static ClaimsPrincipal ToPrincipal(string? accessToken)
    {
        if (accessToken is null)
            return new ClaimsPrincipal(new ClaimsIdentity());

        var payload = accessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
        var userId = json.RootElement.GetProperty("sub").GetString()!;

        return new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)], authenticationType: "Bearer"));
    }

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
