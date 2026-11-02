namespace Pingo.Identity.Models;

public record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public abstract record LoginResult
{
    public sealed record Succeeded(Guid UserId, AuthTokens Tokens) : LoginResult;

    // Deliberately doesn't say whether the email or the password was wrong.
    public sealed record InvalidCredentials : LoginResult;
}
