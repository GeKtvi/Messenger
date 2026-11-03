namespace Pingo.Identity.Models.LoginResults;

public sealed record UserNotFound : LoginResult
{
    public static readonly UserNotFound Default = new();

    private UserNotFound() { }
}
