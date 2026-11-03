using Pingo.Identity.Data;

namespace Pingo.Identity.ComponentTests;

// Wraps the real repository and holds every FindAsync until the expected number of requests have
// found the token. That forces the dangerous race on purpose: all of them see a valid token before
// any of them replaces it, so only the replace step's own check can stop a double use.
internal sealed class FindGateRefreshTokenRepository(IRefreshTokenRepository inner, int requests)
    : IRefreshTokenRepository
{
    private readonly TaskCompletionSource _allFound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _found;

    public Task AddAsync(StoredRefreshToken token, CancellationToken ct = default) => inner.AddAsync(token, ct);

    public async Task<StoredRefreshToken?> FindAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        var token = await inner.FindAsync(tokenHash, ct);
        if (Interlocked.Increment(ref _found) == requests)
            _allFound.TrySetResult();

        // Fails the request instead of hanging the test if fewer requests than expected arrive.
        await _allFound.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        return token;
    }

    public Task<bool> TryReplaceAsync(byte[] oldTokenHash, StoredRefreshToken replacement, CancellationToken ct = default) =>
        inner.TryReplaceAsync(oldTokenHash, replacement, ct);
}
