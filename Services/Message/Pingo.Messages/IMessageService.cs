using Pingo.Messages.Models;

namespace Pingo.Messages;

public interface IMessageService
{
    Task<IReadOnlyList<Message>> GetAllAsync(CancellationToken ct = default);

    Task<Message> SendAsync(string text, CancellationToken ct = default);
}
