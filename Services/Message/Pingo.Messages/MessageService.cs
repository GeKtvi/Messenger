using Microsoft.EntityFrameworkCore;
using Pingo.Messages.Data;
using MessageEntity = Pingo.Messages.Entities.Message;
using Pingo.Messages.Models;

namespace Pingo.Messages;

internal class MessageService(MessageDbContext db, TimeProvider time) : IMessageService
{
    // Scaffold only: loads and returns every message with no paging or limit.
    // Fine for the first stage; replace with a paged/cursor-based query before real use.
    public async Task<IReadOnlyList<Message>> GetAllAsync(CancellationToken ct = default) =>
        await db.Messages
            .AsNoTracking()
            .OrderBy(m => m.SentAt)
            .Select(m => new Message(m.Id, m.Text, m.SentAt))
            .ToListAsync(ct);

    public async Task<Message> SendAsync(string text, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(text.Length, MessageLimits.MaxTextLength, nameof(text));

        var message = new MessageEntity
        {
            Id = Guid.NewGuid(),
            Text = text,
            SentAt = time.GetUtcNow()
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        return new Message(message.Id, message.Text, message.SentAt);
    }
}
