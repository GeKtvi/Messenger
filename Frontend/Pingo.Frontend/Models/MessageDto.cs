namespace Pingo.Frontend.Models;

public record MessageDto(Guid Id, string Text, DateTimeOffset SentAt, DateTimeOffset? UpdatedAt);

public record SendMessageRequest(string Text);
