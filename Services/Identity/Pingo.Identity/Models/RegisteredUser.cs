namespace Pingo.Identity.Models;

public sealed record RegisteredUser(Guid UserId, DateTimeOffset RegisteredAt);
