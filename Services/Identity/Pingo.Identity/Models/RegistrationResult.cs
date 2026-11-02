namespace Pingo.Identity.Models;

public abstract record RegistrationResult
{
    public sealed record Registered(Guid UserId, DateTimeOffset RegisteredAt) : RegistrationResult;

    public sealed record EmailTaken : RegistrationResult;
}
