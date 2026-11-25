namespace Pingo.Frontend.Services.Auth;

// The signed-in session. The access token lives only in memory, so a page reload drops it and the
// session is restored from the HttpOnly refresh token cookie instead. A singleton: in Blazor
// WebAssembly, IHttpClientFactory creates message handlers in their own scope, and they must see
// the same token as the rest of the app.
public class AuthSession(IdentityApiClient identity, IRefreshLock refreshLock) : IAuthSession
{
    private readonly object _refreshGate = new();
    private Task? _restore;
    private Task? _refreshing;

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
    // Refresh tokens work once, so two parallel refreshes would spend the cookie twice and the second
    // would be refused. Within a tab, callers that arrive while a refresh is running share it; between
    // tabs, which share the cookie, the refresh lock makes them take turns.
    public Task RefreshAsync()
    {
        lock (_refreshGate)
        {
            if (_refreshing is { IsCompleted: false })
                return _refreshing;
            return _refreshing = RunRefreshAsync();
        }
    }

    private async Task RunRefreshAsync()
    {
        // By the time another tab releases the lock, the browser has stored the cookie it got.
        await using var held = await refreshLock.AcquireAsync();
        try
        {
            var tokens = await identity.RefreshAsync();
            if (tokens is null)
                SignOut();
            else
                SignIn(tokens.AccessToken);
        }
        // TaskCanceledException: the request timed out.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Identity unreachable or failing, which says nothing about the session: stay as we are.
            // The next request that gets a 401 tries again.
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
