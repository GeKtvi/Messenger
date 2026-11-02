namespace Pingo.Identity.Data;

internal interface IRefreshTokenRepository
{
    // Login: stores a newly issued token.
    Task AddAsync(StoredRefreshToken token, CancellationToken ct = default);

    // Refresh: finds a token by its hash, or null if it doesn't exist. Expiry is checked by the
    // caller against TimeProvider, so tests can control "now".
    Task<StoredRefreshToken?> FindAsync(byte[] tokenHash, CancellationToken ct = default);

    // Refresh with rotation: deletes the old token and stores its replacement in one transaction.
    // Returns false, with nothing changed, if the old token was already gone (for example, used by
    // another request a moment earlier), so one refresh token can't be redeemed twice.
    Task<bool> TryReplaceAsync(byte[] oldTokenHash, StoredRefreshToken replacement, CancellationToken ct = default);
}
