using Pingo.Identity.Models;

namespace Pingo.Identity;

public interface IAuthService
{
    // Creates a user with an email and password sign-in. Emails are compared case-insensitively.
    Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken ct = default);
}
