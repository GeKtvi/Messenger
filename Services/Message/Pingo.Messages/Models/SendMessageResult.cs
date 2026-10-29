namespace Pingo.Messages.Models;

// Created is true when the message didn't exist before this call.
public record SendMessageResult(Message Message, bool Created);
