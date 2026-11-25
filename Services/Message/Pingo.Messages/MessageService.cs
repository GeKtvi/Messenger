using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pingo.Messages.Data;
using Pingo.Messages.Models;
using MessageEntity = Pingo.Messages.Entities.Message;

namespace Pingo.Messages;

internal class MessageService(MessageDbContext db, ReadOnlyMessageDbContext readDb, TimeProvider time) : IMessageService
{
    // Scaffold only: loads and returns every message with no paging or limit.
    // Fine for the first stage; replace with a paged/cursor-based query before real use.
    public async Task<IReadOnlyList<Message>> GetAllAsync(CancellationToken ct = default) =>
        await readDb.Messages
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Select(m => new Message(m.Id, m.Text, m.SentAt, m.UpdatedAt))
            .ToListAsync(ct);

    public async Task<SendMessageResult> SendAsync(Guid id, string text, CancellationToken ct = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Message id must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(text.Length, MessageLimits.MaxTextLength, nameof(text));

        // Not on the replica: it may lag behind and miss a message that exists.
        var message = await db.Messages.FindAsync([id], ct);
        if (message is null)
        {
            message = new MessageEntity { Id = id, Text = text, SentAt = Now() };
            db.Messages.Add(message);
            try
            {
                await db.SaveChangesAsync(ct);
                return new SendMessageResult(ToModel(message), Created: true);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // A concurrent request created the same id first: fall through and update it instead.
                db.ChangeTracker.Clear();
                message = await db.Messages.FindAsync([id], ct)
                    ?? throw new InvalidOperationException($"Message {id} vanished after a duplicate insert.", ex);
            }
        }

        // Repeating the same request must not change anything, so only a new text counts as an edit.
        if (message.Text != text)
        {
            message.Text = text;
            message.UpdatedAt = Now();
            await db.SaveChangesAsync(ct);
        }

        return new SendMessageResult(ToModel(message), Created: false);
    }

    // PostgreSQL stores times to the microsecond. Rounding down here keeps the response to a send
    // identical to what later reads of the same message return.
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static Message ToModel(MessageEntity message) =>
        new(message.Id, message.Text, message.SentAt, message.UpdatedAt);
}
