namespace Pingo.Messages.Entities;

internal class Message
{
    public Guid Id { get; init; }
    public required string Text { get; set; }
    public DateTimeOffset SentAt { get; init; }
}
