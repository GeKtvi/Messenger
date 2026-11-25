using Pingo.Messages.Models;

namespace Pingo.Messages;

// Told about every message that was created or got a new text, so open chats can show it at once.
// The API layer implements it; without one, nobody is told.
public interface IMessageNotifier
{
    Task MessageSavedAsync(Message message, CancellationToken ct = default);
}

internal sealed class NoMessageNotifier : IMessageNotifier
{
    public Task MessageSavedAsync(Message message, CancellationToken ct = default) => Task.CompletedTask;
}
