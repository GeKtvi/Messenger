namespace Pingo.Identity.Data;

internal interface IUserRepository
{
    // Registration: inserts the user and their password login in one transaction. Returns false,
    // with nothing stored, if the email is already taken.
    Task<bool> TryCreateWithPasswordLoginAsync(User user, PasswordLogin login, CancellationToken ct = default);
}
