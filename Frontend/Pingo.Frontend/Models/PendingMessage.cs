namespace Pingo.Frontend.Models;

public enum PendingStatus
{
    Sending,
    Failed
}

// A message the user sent that the server hasn't confirmed yet.
public class PendingMessage
{
    public required Guid Id { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public PendingStatus Status { get; set; } = PendingStatus.Sending;
}
