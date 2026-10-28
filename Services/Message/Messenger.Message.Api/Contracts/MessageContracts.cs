namespace Messenger.Message.Api.Contracts;

public record SendMessageRequest(string Text);

public record MessageResponse(Guid Id, string Text, DateTimeOffset SentAt);
