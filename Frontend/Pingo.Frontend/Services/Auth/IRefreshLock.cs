using Microsoft.JSInterop;

namespace Pingo.Frontend.Services.Auth;

// Taken around a token refresh. All tabs share one refresh token cookie and each token works once,
// so only one tab may refresh at a time.
public interface IRefreshLock
{
    // Waits for the lock; disposing the result releases it.
    Task<IAsyncDisposable> AcquireAsync();
}

// The browser's Web Locks API, through wwwroot/js/refresh-lock.js. The lock is shared by every tab of
// the same origin, and the browser releases it if its tab closes.
public sealed class BrowserRefreshLock(IJSRuntime js) : IRefreshLock
{
    public async Task<IAsyncDisposable> AcquireAsync() =>
        new Held(await js.InvokeAsync<IJSObjectReference>("pingoRefreshLock.acquire"));

    private sealed class Held(IJSObjectReference handle) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await handle.InvokeVoidAsync("release");
            await handle.DisposeAsync();
        }
    }
}
