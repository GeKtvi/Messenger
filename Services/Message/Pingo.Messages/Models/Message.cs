namespace Pingo.Messages.Models;

public record Message(Guid Id, string Text, DateTimeOffset SentAt);
