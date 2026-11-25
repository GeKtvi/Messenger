using System.Text.Json;
using Microsoft.JSInterop;
using Polly.Timeout;
using Pingo.Frontend.Models;

namespace Pingo.Frontend.Services;

// Messages waiting for the server, like a messenger's outbox. They show up in the chat right away,
// are saved in localStorage so they survive a reload, and are resent when the app starts.
// Resending is safe because PUT /api/messages/{id} is idempotent: a message that already
// reached the server is never duplicated.
//
// A confirmed message is kept too until a loaded list contains it: lists may come from a database that
// lags a little behind, and a message the user has just sent must not disappear on a reload (D-069).
public class MessageOutbox(MessageApiClient api, IJSRuntime js)
{
    private const string StorageKey = "pingo.outbox";

    private readonly List<PendingMessage> _pending = [];
    private readonly List<MessageDto> _delivered = [];
    private bool _initialized;

    public IReadOnlyList<PendingMessage> Pending => _pending;

    // Raised whenever a message is added, removed or changes its status.
    public event Action? Changed;

    // Raised after the server has confirmed a message.
    public event Action? Delivered;

    // Restores saved messages and resends them. Safe to call more than once.
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        foreach (var saved in await LoadAsync())
        {
            if (saved.Delivered is null)
                _pending.Add(new PendingMessage { Id = saved.Id, Text = saved.Text, CreatedAt = saved.CreatedAt });
            else
                _delivered.Add(saved.Delivered);
        }

        Changed?.Invoke();
        foreach (var message in _pending.ToList())
            _ = DeliverAsync(message);
    }

    // The chat to show: the loaded list plus delivered messages it doesn't contain yet, oldest first.
    public IReadOnlyList<MessageDto> Merge(IReadOnlyList<MessageDto> loaded)
    {
        var ids = loaded.Select(m => m.Id).ToHashSet();
        var missing = _delivered.Where(m => !ids.Contains(m.Id)).ToList();
        // OrderBy keeps the loaded order for messages with the same time.
        return missing.Count == 0 ? loaded : [.. loaded.Concat(missing).OrderBy(m => m.SentAt)];
    }

    // Call after loading a list: delivered messages it contains don't need to be kept any longer.
    public async Task ForgetListedAsync(IReadOnlyList<MessageDto> loaded)
    {
        var ids = loaded.Select(m => m.Id).ToHashSet();
        if (_delivered.RemoveAll(m => ids.Contains(m.Id)) == 0)
            return;

        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task SendAsync(string text)
    {
        var message = new PendingMessage { Id = Guid.NewGuid(), Text = text.Trim(), CreatedAt = DateTimeOffset.UtcNow };
        _pending.Add(message);
        await SaveAsync();
        Changed?.Invoke();

        _ = DeliverAsync(message);
    }

    public Task RetryAsync(PendingMessage message) => DeliverAsync(message);

    // On logout: these messages belong to the user who wrote them, so the next one doesn't get them.
    public async Task ClearAsync()
    {
        _pending.Clear();
        _delivered.Clear();
        _initialized = false;
        await SaveAsync();
        Changed?.Invoke();
    }

    public async Task DiscardAsync(PendingMessage message)
    {
        _pending.Remove(message);
        await SaveAsync();
        Changed?.Invoke();
    }

    private async Task DeliverAsync(PendingMessage message)
    {
        var confirmed = false;
        message.Status = PendingStatus.Sending;
        Changed?.Invoke();

        try
        {
            var delivered = await api.SendAsync(message.Id, message.Text);
            // Not in the list any more: the user has logged out meanwhile.
            if (_pending.Remove(message))
            {
                _delivered.Add(delivered);
                await SaveAsync();
                confirmed = true;
            }
        }
        // TimeoutRejectedException: the retry pipeline gives up because every attempt took too long.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutRejectedException)
        {
            message.Status = PendingStatus.Failed;
        }

        Changed?.Invoke();
        if (confirmed)
            Delivered?.Invoke();
    }

    // Delivered is the server's copy once it has confirmed the message.
    private record SavedMessage(Guid Id, string Text, DateTimeOffset CreatedAt, MessageDto? Delivered = null);

    private async Task<List<SavedMessage>> LoadAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            return json is null ? [] : JsonSerializer.Deserialize<List<SavedMessage>>(json) ?? [];
        }
        catch (Exception ex) when (ex is JSException or JsonException)
        {
            // Storage unavailable (private mode) or corrupted: start with an empty outbox.
            return [];
        }
    }

    private async Task SaveAsync()
    {
        var saved = _pending.Select(m => new SavedMessage(m.Id, m.Text, m.CreatedAt))
            .Concat(_delivered.Select(m => new SavedMessage(m.Id, m.Text, m.SentAt, m)));
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(saved));
        }
        catch (JSException)
        {
            // Storage unavailable: these messages just won't survive a reload.
        }
    }
}
