using CSharpFunctionalExtensions;
using Pingo.Identity.Data;
using Pingo.Identity.Models;
using Pingo.Identity.Passwords;
using Pingo.Identity.Tokens;

namespace Pingo.Identity;

// The registration and login flows. SQL and connections live in the repositories; each repository
// call takes a pooled connection and returns it, so none is held during the slow password hashing.
// Each flow returns a Result: a value, or the error enum value; both convert to it implicitly.
internal class AuthService(
    IUserRepository users,
    IPasswordLoginRepository passwordLogins,
    IRefreshTokenRepository refreshTokens,
    TokenFactory tokens,
    TimeProvider time) : IAuthService
{
    public async Task<Result<RegisteredUser, RegistrationError>> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var now = Now();
        var user = new User(Guid.CreateVersion7(), now);
        var login = new PasswordLogin(user.Id, NormalizeEmail(email), PasswordHasher.Hash(password), now);

        return await users.TryCreateWithPasswordLoginAsync(user, login, ct)
            ? new RegisteredUser(user.Id, user.RegisteredAt)
            : RegistrationError.EmailTaken;
    }

    public async Task<Result<AuthTokens, LoginError>> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var login = await passwordLogins.FindByEmailAsync(NormalizeEmail(email), ct);
        if (login is null)
            return LoginError.UserNotFound;
        if (!PasswordHasher.Verify(password, login.Password))
            return LoginError.WrongPassword;

        var now = Now();
        var accessToken = tokens.CreateAccessToken(login.UserId, now);
        var refreshToken = tokens.CreateRefreshToken(now);
        await refreshTokens.AddAsync(new StoredRefreshToken(refreshToken.Hash, login.UserId, now, refreshToken.ExpiresAt), ct);

        return new AuthTokens(accessToken.Value, accessToken.ExpiresAt, refreshToken.Value, refreshToken.ExpiresAt);
    }

    public async Task<Result<AuthTokens, RefreshError>> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var now = Now();
        var oldHash = TokenFactory.HashRefreshToken(refreshToken);
        var stored = await refreshTokens.FindAsync(oldHash, ct);
        if (stored is null || stored.ExpiresAt <= now)
            return RefreshError.InvalidToken;

        // The new refresh token keeps the old expiry: a session lasts RefreshTokenLifetime from login,
        // however often it's refreshed, so a stolen token can't keep a session alive forever.
        var accessToken = tokens.CreateAccessToken(stored.UserId, now);
        var newRefreshToken = tokens.CreateRefreshToken(now) with { ExpiresAt = stored.ExpiresAt };
        var replacement = new StoredRefreshToken(newRefreshToken.Hash, stored.UserId, now, newRefreshToken.ExpiresAt);

        if (!await refreshTokens.TryReplaceAsync(oldHash, replacement, ct))
            return RefreshError.InvalidToken;

        return new AuthTokens(
            accessToken.Value, accessToken.ExpiresAt, newRefreshToken.Value, newRefreshToken.ExpiresAt);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        return refreshTokens.DeleteAsync(TokenFactory.HashRefreshToken(refreshToken), ct);
    }

    // Emails are case-insensitive in practice, so "Ann@Example.com" and "ann@example.com" are one account.
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // PostgreSQL stores times to the microsecond, so round down to match what reads return.
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
