namespace Pingo.Identity.Models.RegistrationResults;

public sealed record EmailTaken : RegistrationResult
{
    public static readonly EmailTaken Default = new();

    private EmailTaken() { }
}
