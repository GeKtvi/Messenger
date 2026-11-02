using Dapper;
using Npgsql;
using Pingo.Identity.Data;
using Pingo.Identity.Models;
using Pingo.Identity.Passwords;

namespace Pingo.Identity;

internal class AuthService(IdentityDatabase database, TimeProvider time) : IAuthService
{
    private const string InsertUserSql = """
        INSERT INTO users (id, registered_at)
        VALUES (@UserId, @Now);
        """;

    private const string InsertPasswordLoginSql = """
        INSERT INTO password_logins (user_id, email, password_hash, password_salt, password_iterations, created_at)
        VALUES (@UserId, @Email, @Hash, @Salt, @Iterations, @Now);
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

    // Emails are case-insensitive in practice, so "Ann@Example.com" and "ann@example.com" are one account.
    internal static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    // PostgreSQL stores times to the microsecond, so round down to match what reads return.
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
