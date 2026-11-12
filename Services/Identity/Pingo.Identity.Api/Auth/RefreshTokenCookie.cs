namespace Pingo.Identity.Api.Auth;

// The refresh token travels only in this cookie, never in a response body, so scripts on the page
// can't read it (D-054):
// - HttpOnly: JavaScript can't read it, so an injected script can't steal the 30-day session.
// - Secure: only sent over HTTPS (browsers also allow http://localhost for development).
// - SameSite=Strict: only sent on requests from our own site, which also blocks CSRF.
// - Path=/api/auth: only sent to the auth endpoints, not with every request.
public static class RefreshTokenCookie
{
    public const string Name = "pingo_refresh";
    private const string Path = "/api/auth";

    // A real token is 43 characters; anything much longer isn't worth hashing.
    private const int MaxLength = 256;

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && value.Length is > 0 and <= MaxLength ? value : null;

    public static void Write(HttpResponse response, string refreshToken, DateTimeOffset expiresAt) =>
        response.Cookies.Append(Name, refreshToken, Options(expiresAt));

    public static void Delete(HttpResponse response) => response.Cookies.Delete(Name, Options(expiresAt: null));

    private static CookieOptions Options(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
