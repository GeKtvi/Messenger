namespace Pingo.Identity.Models;

public enum RefreshError
{
    // Unknown, expired or already used: the client has to log in again.
    InvalidToken,
}
