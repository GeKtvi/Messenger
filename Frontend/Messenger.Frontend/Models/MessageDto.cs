namespace Messenger.Frontend.Models;

public record MessageDto(Guid Id, string Text, DateTimeOffset SentAt);

public record SendMessageRequest(string Text);
