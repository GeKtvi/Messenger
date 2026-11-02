namespace Pingo.Identity.Models;

public abstract record RefreshResult
{
    public sealed record Succeeded(Guid UserId, AuthTokens Tokens) : RefreshResult;

    // Unknown, expired or already used: the client has to log in again.
    public sealed record InvalidToken : RefreshResult;
}
