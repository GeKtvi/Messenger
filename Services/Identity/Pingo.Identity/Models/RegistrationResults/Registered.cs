namespace Pingo.Identity.Models.RegistrationResults;

public sealed record Registered(Guid UserId, DateTimeOffset RegisteredAt) : RegistrationResult;
