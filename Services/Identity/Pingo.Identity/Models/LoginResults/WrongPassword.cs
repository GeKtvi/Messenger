namespace Pingo.Identity.Models.LoginResults;

public sealed record WrongPassword : LoginResult
{
    public static readonly WrongPassword Default = new();

    private WrongPassword() { }
}
