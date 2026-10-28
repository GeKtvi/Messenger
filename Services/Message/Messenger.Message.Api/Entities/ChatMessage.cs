namespace Messenger.Message.Api.Entities;

public class ChatMessage
{
    public Guid Id { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
