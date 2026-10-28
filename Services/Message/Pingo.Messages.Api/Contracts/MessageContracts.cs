using System.ComponentModel.DataAnnotations;

namespace Pingo.Messages.Api.Contracts;

// A class with properties (not a bare string or a positional record) so new optional
// fields can be added later without breaking existing clients.
public class SendMessageRequest
{
    [Required(ErrorMessage = "Message text is required.")]
    [MaxLength(MessageLimits.MaxTextLength, ErrorMessage = "Message text must be at most {1} characters.")]
    public string Text { get; init; } = string.Empty;
}

public record MessageResponse(Guid Id, string Text, DateTimeOffset SentAt, DateTimeOffset? UpdatedAt);
