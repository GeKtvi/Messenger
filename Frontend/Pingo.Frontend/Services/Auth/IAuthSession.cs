namespace Pingo.Frontend.Services.Auth;

// The signed-in session as the rest of the app sees it: the current access token, and the actions
// that change it. The refresh token is never exposed: it stays in Identity's HttpOnly cookie.
public interface IAuthSession
{
    // The access token for "Authorization: Bearer", or null when nobody is signed in.
    string? AccessToken { get; }

    bool IsSignedIn { get; }

    // Raised when the user signs in or out.
    event Action? Changed;

    // On app start: signs in from the refresh token cookie if it's still valid. Runs once.
    Task RestoreAsync();

    // Returns null on success, or the server's (localized) message.
    Task<string?> LoginAsync(string email, string password);

    // Registers, then logs straight in. Returns null on success, or the server's message.
    Task<string?> RegisterAsync(string email, string password);

    Task LogoutAsync();

    // Gets a new access token from the cookie, or signs out if the session has ended. Callers that
    // arrive while a refresh is running share it.
    Task RefreshAsync();
}
