using CSharpFunctionalExtensions;
using Pingo.Identity.Models;

namespace Pingo.Identity;

public interface IAuthService
{
    // Creates a user with an email and password sign-in. Emails are compared case-insensitively.
    Task<Result<RegisteredUser, RegistrationError>> RegisterAsync(
        string email, string password, CancellationToken ct = default);

    // Checks an email and password and issues an access token (JWT) and a refresh token.
    Task<Result<AuthTokens, LoginError>> LoginAsync(
        string email, string password, CancellationToken ct = default);

    // Exchanges a refresh token for a new access token and a new refresh token. The old refresh
    // token stops working (rotation).
    Task<Result<AuthTokens, RefreshError>> RefreshAsync(
        string refreshToken, CancellationToken ct = default);
}
