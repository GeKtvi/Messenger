using Polly.Timeout;
using Pingo.Frontend.Models;

namespace Pingo.Frontend.Services;

// The messages the open chat shows. Loads the list when the chat opens and again after each message
// the user has sent. Lists may come from databases that lag behind by different amounts, so a new list
// is merged into what is already shown and never takes anything away (D-069).
public class MessageFeed
{
    private readonly MessageApiClient _api;
    private readonly MessageOutbox _outbox;

    // Every message listed since the chat was opened; null until the first load has finished.
    private List<MessageDto>? _listed;

    private bool _open;

    // Changes when the chat is closed, so that an answer to an earlier request is recognized as outdated.
    private int _session;

    private bool _loading;
    private bool _loadAgain;
    private Task _load = Task.CompletedTask;

    public MessageFeed(MessageApiClient api, MessageOutbox outbox)
    {
        _api = api;
        _outbox = outbox;
        outbox.Changed += () => Changed?.Invoke();
        outbox.Delivered += OnDelivered;
    }

    // Raised whenever what the chat shows has changed.
    public event Action? Changed;

    // Why the latest load failed, or null if it didn't.
    public string? LoadError { get; private set; }

    // Null until the first load has finished.
    public IReadOnlyList<MessageDto>? Messages => _listed is null ? null : _outbox.Merge(_listed);

    // A pending message can also be listed already (e.g. the server's answer was lost); show it only once.
    public IEnumerable<PendingMessage> Pending =>
        _outbox.Pending.Where(p => _listed?.Any(m => m.Id == p.Id) != true);

    public async Task OpenAsync()
    {
        _open = true;
        // The outbox first, so the list never appears without the user's own messages.
        await _outbox.InitializeAsync();
        // Closed again meanwhile.
        if (!_open)
            return;

        await LoadAsync();
    }

    // Forgets everything: whoever opens the chat next, possibly another user, starts from a fresh load.
    public void Close()
    {
        _open = false;
        _session++;
        _loadAgain = false;
        _listed = null;
        LoadError = null;
    }

    // One load at a time. If more are asked for meanwhile, one more runs afterwards, which is enough to
    // see everything that happened during the first.
    public Task LoadAsync()
    {
        if (_loading)
        {
            _loadAgain = true;
            return _load;
        }

        return _load = LoadUntilCurrentAsync();
    }

    private void OnDelivered()
    {
        if (_open)
            _ = LoadAsync();
    }

    private async Task LoadUntilCurrentAsync()
    {
        _loading = true;
        try
        {
            do
            {
                _loadAgain = false;
                await LoadOnceAsync();
            }
            while (_loadAgain);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadOnceAsync()
    {
        var session = _session;
        try
        {
            var loaded = await _api.GetAllAsync();
            if (session != _session)
                return;

            _listed = Combine(_listed ?? [], loaded);
            LoadError = null;
            await _outbox.ForgetListedAsync(loaded);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutRejectedException)
        {
            if (session != _session)
                return;

            // What is already shown stays.
            _listed ??= [];
            LoadError = ex.Message;
        }

        Changed?.Invoke();
    }

    // All messages of both lists. Of two copies the one edited later wins, otherwise the newly loaded one.
    private static List<MessageDto> Combine(IReadOnlyList<MessageDto> shown, IReadOnlyList<MessageDto> loaded)
    {
        var byId = shown.ToDictionary(m => m.Id);
        foreach (var message in loaded)
        {
            if (!byId.TryGetValue(message.Id, out var known) || EditedAt(message) >= EditedAt(known))
                byId[message.Id] = message;
        }

        // The server's order.
        return [.. byId.Values.OrderBy(m => m.SentAt).ThenBy(m => m.Id)];
    }

    private static DateTimeOffset EditedAt(MessageDto message) => message.UpdatedAt ?? DateTimeOffset.MinValue;
}
