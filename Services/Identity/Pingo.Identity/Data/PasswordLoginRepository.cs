using Dapper;
using Pingo.Identity.Passwords;

namespace Pingo.Identity.Data;

internal class PasswordLoginRepository(IdentityDatabase database) : IPasswordLoginRepository
{
    // Column names are aliased to the Row's property names, so Dapper can map them.
    private const string FindByEmailSql = """
        SELECT user_id AS UserId, email AS Email, password_hash AS Hash, password_salt AS Salt,
               password_iterations AS Iterations, created_at AS CreatedAt
        FROM password_logins
        WHERE email = @Email;
        """;

    // Npgsql reads timestamptz as a UTC DateTime, and Dapper's constructor mapping needs exact types,
    // so the row uses DateTime and is converted to DateTimeOffset below.
    private record Row(Guid UserId, string Email, byte[] Hash, byte[] Salt, int Iterations, DateTime CreatedAt);

    public Task<PasswordLogin?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        database.RunAsync(async connection =>
        {
            var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
                FindByEmailSql, new { Email = normalizedEmail }, cancellationToken: ct));

            return row is null
                ? null
                : new PasswordLogin(row.UserId, row.Email, new PasswordHash(row.Hash, row.Salt, row.Iterations), new DateTimeOffset(row.CreatedAt));
        }, ct);
}
