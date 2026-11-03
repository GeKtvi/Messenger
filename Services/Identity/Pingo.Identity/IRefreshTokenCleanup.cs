namespace Pingo.Identity;

// Removes refresh tokens that can no longer be used. Run by a daily background job; public so the
// API project can schedule it.
public interface IRefreshTokenCleanup
{
    // Deletes every refresh token whose expiry is already in the past. Returns how many it deleted.
    Task<int> DeleteExpiredAsync(CancellationToken ct = default);
}
