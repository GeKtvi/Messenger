using Pingo.Messages.Models;

namespace Pingo.Messages;

public interface IMessageService
{
    Task<IReadOnlyList<Message>> GetAllAsync(CancellationToken ct = default);

    // Idempotent: creates the message if the id is new, otherwise updates its text.
    Task<SendMessageResult> SendAsync(Guid id, string text, CancellationToken ct = default);
}
