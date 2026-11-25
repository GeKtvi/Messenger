using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Tests;

// The browser's lock shared by all tabs: sessions given the same instance act like tabs of one browser.
public sealed class FakeRefreshLock : IRefreshLock
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IAsyncDisposable> AcquireAsync()
    {
        await _gate.WaitAsync();
        return new Held(_gate);
    }

    private sealed class Held(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
