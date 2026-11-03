using Microsoft.Extensions.Logging;
using Pingo.Identity.Data;

namespace Pingo.Identity;

// Expired tokens are already rejected by RefreshAsync; deleting them only keeps the table small.
// A token expiring exactly now is left for the next run, matching "ExpiresAt < now".
internal sealed class RefreshTokenCleanup(
    IRefreshTokenRepository refreshTokens,
    TimeProvider time,
    ILogger<RefreshTokenCleanup> logger) : IRefreshTokenCleanup
{
    public async Task<int> DeleteExpiredAsync(CancellationToken ct = default)
    {
        var deleted = await refreshTokens.DeleteExpiredAsync(time.GetUtcNow(), ct);
        logger.LogInformation("Deleted {Count} expired refresh tokens", deleted);
        return deleted;
    }
}
