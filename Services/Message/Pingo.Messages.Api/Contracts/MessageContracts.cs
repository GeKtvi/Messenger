using System.ComponentModel.DataAnnotations;
using Pingo.Messages;

namespace Pingo.Messages.Api.Contracts;

public record SendMessageRequest(
    [Required(ErrorMessage = "Message text is required.")]
    [MaxLength(MessageLimits.MaxTextLength, ErrorMessage = "Message text must be at most {1} characters.")]
    string Text);

public record MessageResponse(Guid Id, string Text, DateTimeOffset SentAt);
