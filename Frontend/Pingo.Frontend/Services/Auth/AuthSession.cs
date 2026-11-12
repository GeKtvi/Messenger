namespace Pingo.Frontend.Services.Auth;

// The signed-in session. The access token lives only in memory, so a page reload drops it and the
// session is restored from the HttpOnly refresh token cookie instead. A singleton: in Blazor
// WebAssembly, IHttpClientFactory creates message handlers in their own scope, and they must see
// the same token as the rest of the app.
public class AuthSession(IdentityApiClient identity)
{
    private Task? _restore;

    public string? AccessToken { get; private set; }

    public bool IsSignedIn => AccessToken is not null;

    // Raised when the user signs in or out.
    public event Action? Changed;

    // On app start: if the browser still has a valid refresh token cookie, sign in without a password.
    public Task RestoreAsync() => _restore ??= RefreshAsync();

    public async Task<string?> LoginAsync(string email, string password)
    {
        var outcome = await identity.LoginAsync(email, password);
        if (outcome.Succeeded)
            SignIn(outcome.Tokens!.AccessToken);
        return outcome.Error;
    }

    // Registers, then logs straight in. Returns null on success, or the server's message.
    public async Task<string?> RegisterAsync(string email, string password)
    {
        return await identity.RegisterAsync(email, password) ?? await LoginAsync(email, password);
    }

    public async Task LogoutAsync()
    {
        try
        {
            await identity.LogoutAsync();
        }
        catch (HttpRequestException)
        {
            // Identity unreachable: still sign out here. The refresh token expires on its own.
        }

        SignOut();
    }

    // Gets a new access token with the refresh token cookie; signs out if the session has ended.
    public async Task RefreshAsync()
    {
        try
        {
            var tokens = await identity.RefreshAsync();
            if (tokens is null)
                SignOut();
            else
                SignIn(tokens.AccessToken);
        }
        catch (HttpRequestException)
        {
            SignOut();
        }
    }

    private void SignIn(string accessToken)
    {
        AccessToken = accessToken;
        Changed?.Invoke();
    }

    private void SignOut()
    {
        var wasSignedIn = IsSignedIn;
        AccessToken = null;
        if (wasSignedIn)
            Changed?.Invoke();
    }
}
