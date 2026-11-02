namespace Pingo.Identity.Data;

internal interface IPasswordLoginRepository
{
    // Login: null if no account uses this email.
    Task<PasswordLogin?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default);
}
