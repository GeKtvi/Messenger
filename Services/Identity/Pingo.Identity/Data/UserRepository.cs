using Dapper;
using Npgsql;

namespace Pingo.Identity.Data;

internal class UserRepository(IdentityDatabase database) : IUserRepository
{
    private const string InsertUserSql = """
        INSERT INTO users (id, registered_at)
        VALUES (@Id, @RegisteredAt);
        """;

    private const string InsertPasswordLoginSql = """
        INSERT INTO password_logins (user_id, email, password_hash, password_salt, password_iterations, created_at)
        VALUES (@UserId, @Email, @Hash, @Salt, @Iterations, @CreatedAt);
        """;

    private const string EmailUniqueConstraint = "password_logins_email_key";

    public async Task<bool> TryCreateWithPasswordLoginAsync(User user, PasswordLogin login, CancellationToken ct = default)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(InsertUserSql, user, transaction, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(InsertPasswordLoginSql, new
            {
                login.UserId,
                Email = login.NormalizedEmail,
                login.Password.Hash,
                login.Password.Salt,
                login.Password.Iterations,
                login.CreatedAt,
            }, transaction, cancellationToken: ct));
            await transaction.CommitAsync(ct);
            return true;
        }
        catch (PostgresException ex) when (ex is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: EmailUniqueConstraint })
        {
            // The transaction rolls back on dispose, so the user row is removed too.
            return false;
        }
    }
}
