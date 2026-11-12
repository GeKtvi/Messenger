using System.Text.Json;
using Microsoft.JSInterop;
using Pingo.Frontend.Models;

namespace Pingo.Frontend.Services;

// Messages waiting for the server, like a messenger's outbox. They show up in the chat right away,
// are saved in localStorage so they survive a reload, and are resent when the app starts.
// Resending is safe because PUT /api/messages/{id} is idempotent: a message that already
// reached the server is never duplicated.
public class MessageOutbox(MessageApiClient api, IJSRuntime js)
{
    private const string StorageKey = "pingo.outbox";

    private readonly List<PendingMessage> _pending = [];
    private bool _initialized;

    public IReadOnlyList<PendingMessage> Pending => _pending;

    // Raised whenever the pending list or a status changes.
    public event Action? Changed;

    // Raised with the server's copy when a message is confirmed.
    public event Action<MessageDto>? Delivered;

    // Restores saved messages and resends them. Safe to call more than once.
    public async Task InitializeAsync()
    {
        if (_initialized)
            return;
        _initialized = true;

        foreach (var saved in await LoadAsync())
            _pending.Add(new PendingMessage { Id = saved.Id, Text = saved.Text, CreatedAt = saved.CreatedAt });

        Changed?.Invoke();
        foreach (var message in _pending.ToList())
            _ = DeliverAsync(message);
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

    // On logout: unsent messages belong to the user who wrote them, so they don't wait for the next one.
    public async Task ClearAsync()
    {
        _pending.Clear();
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
        message.Status = PendingStatus.Sending;
        Changed?.Invoke();

        try
        {
            var delivered = await api.SendAsync(message.Id, message.Text);
            _pending.Remove(message);
            await SaveAsync();
            Delivered?.Invoke(delivered);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            message.Status = PendingStatus.Failed;
        }

        Changed?.Invoke();
    }

    private record SavedMessage(Guid Id, string Text, DateTimeOffset CreatedAt);

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
        var saved = _pending.Select(m => new SavedMessage(m.Id, m.Text, m.CreatedAt));
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(saved));
        }
        catch (JSException)
        {
            // Storage unavailable: pending messages just won't survive a reload.
        }
    }
}
