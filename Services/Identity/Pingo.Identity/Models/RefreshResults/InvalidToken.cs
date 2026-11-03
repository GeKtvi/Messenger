namespace Pingo.Identity.Models.RefreshResults;

// Unknown, expired or already used: the client has to log in again.
public sealed record InvalidToken : RefreshResult
{
    public static readonly InvalidToken Default = new();

    private InvalidToken() { }
}
