using Dapper;
using Npgsql;
using Pingo.Identity.Data;
using Pingo.Identity.Models;
using Pingo.Identity.Passwords;
using Pingo.Identity.Tokens;

namespace Pingo.Identity;

internal class AuthService(IdentityDatabase database, TokenFactory tokens, TimeProvider time) : IAuthService
{
    private record PasswordLoginRow(Guid UserId, byte[] Hash, byte[] Salt, int Iterations);

    // Checked when the email is unknown, so a login takes as long whether or not the email exists.
    private static readonly Lazy<PasswordHash> DummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString()));

    private const string InsertUserSql = """
        INSERT INTO users (id, registered_at)
        VALUES (@UserId, @Now);
        """;

    private const string InsertPasswordLoginSql = """
        INSERT INTO password_logins (user_id, email, password_hash, password_salt, password_iterations, created_at)
        VALUES (@UserId, @Email, @Hash, @Salt, @Iterations, @Now);
        """;

    private const string SelectPasswordLoginSql = """
        SELECT user_id AS UserId, password_hash AS Hash, password_salt AS Salt, password_iterations AS Iterations
        FROM password_logins
        WHERE email = @Email;
        """;

    private const string InsertRefreshTokenSql = """
        INSERT INTO refresh_tokens (token_hash, user_id, created_at, expires_at)
        VALUES (@Hash, @UserId, @Now, @ExpiresAt);
        """;

    public async Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var normalizedEmail = NormalizeEmail(email);
        var userId = Guid.CreateVersion7();
        var now = Now();
        // Hashing is deliberately slow (PBKDF2), so do it before opening a connection and a transaction.
        var hash = PasswordHasher.Hash(password);

        await using var connection = await database.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InsertUserSql, new { UserId = userId, Now = now }, transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(InsertPasswordLoginSql, new
            {
                UserId = userId,
                Email = normalizedEmail,
                hash.Hash,
                hash.Salt,
                hash.Iterations,
                Now = now,
            }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "password_logins_email_key" })
        {
            // The transaction rolls back on dispose, so the user row is removed too.
            return new RegistrationResult.EmailTaken();
        }

        return new RegistrationResult.Registered(userId, now);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrEmpty(password);

        PasswordLoginRow? login;
        await using (var connection = await database.OpenConnectionAsync(ct))
        {
            login = await connection.QuerySingleOrDefaultAsync<PasswordLoginRow>(new CommandDefinition(
                SelectPasswordLoginSql, new { Email = NormalizeEmail(email) }, cancellationToken: ct));
        }

        // The connection goes back to the pool before the slow hash check, not after it.
        var stored = login is null ? DummyHash.Value : new PasswordHash(login.Hash, login.Salt, login.Iterations);
        if (!PasswordHasher.Verify(password, stored) || login is null)
            return new LoginResult.InvalidCredentials();

        var now = Now();
        var accessToken = tokens.CreateAccessToken(login.UserId, now);
        var refreshToken = tokens.CreateRefreshToken(now);
        await using var insertConnection = await database.OpenConnectionAsync(ct);
        await insertConnection.ExecuteAsync(new CommandDefinition(InsertRefreshTokenSql, new
        {
            refreshToken.Hash,
            login.UserId,
            Now = now,
            refreshToken.ExpiresAt,
        }, cancellationToken: ct));

        return new LoginResult.Succeeded(login.UserId, new AuthTokens(
            accessToken.Value, accessToken.ExpiresAt, refreshToken.Value, refreshToken.ExpiresAt));
    }

    // Emails are case-insensitive in practice, so "Ann@Example.com" and "ann@example.com" are one account.
    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // PostgreSQL stores times to the microsecond, so round down to match what reads return.
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
