using Dapper;

namespace Pingo.Identity.Data;

internal class RefreshTokenRepository(IdentityDatabase database) : IRefreshTokenRepository
{
    private const string InsertSql = """
        INSERT INTO refresh_tokens (token_hash, user_id, created_at, expires_at)
        VALUES (@TokenHash, @UserId, @CreatedAt, @ExpiresAt);
        """;

    private const string FindSql = """
        SELECT token_hash AS TokenHash, user_id AS UserId, created_at AS CreatedAt, expires_at AS ExpiresAt
        FROM refresh_tokens
        WHERE token_hash = @TokenHash;
        """;

    private const string DeleteSql = """
        DELETE FROM refresh_tokens
        WHERE token_hash = @TokenHash;
        """;

    // Uses refresh_tokens_expires_at_idx, so it doesn't scan the whole table.
    private const string DeleteExpiredSql = """
        DELETE FROM refresh_tokens
        WHERE expires_at < @Now;
        """;

    // Npgsql reads timestamptz as a UTC DateTime, and Dapper's constructor mapping needs exact types.
    private record Row(byte[] TokenHash, Guid UserId, DateTime CreatedAt, DateTime ExpiresAt);

    public async Task AddAsync(StoredRefreshToken token, CancellationToken ct = default)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, token, cancellationToken: ct));
    }

    public async Task<StoredRefreshToken?> FindAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            FindSql, new { TokenHash = tokenHash }, cancellationToken: ct));

        return row is null
            ? null
            : new StoredRefreshToken(row.TokenHash, row.UserId, new DateTimeOffset(row.CreatedAt), new DateTimeOffset(row.ExpiresAt));
    }

    public async Task<bool> TryReplaceAsync(byte[] oldTokenHash, StoredRefreshToken replacement, CancellationToken ct = default)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // The DELETE is the check: of two requests using the same token at once, PostgreSQL lets
        // only one delete the row. The other waits for the row lock, then deletes nothing.
        var deleted = await connection.ExecuteAsync(new CommandDefinition(
            DeleteSql, new { TokenHash = oldTokenHash }, transaction, cancellationToken: ct));
        if (deleted == 0)
            return false;

        await connection.ExecuteAsync(new CommandDefinition(InsertSql, replacement, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task DeleteAsync(byte[] tokenHash, CancellationToken ct = default)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(DeleteSql, new { TokenHash = tokenHash }, cancellationToken: ct));
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(DeleteExpiredSql, new { Now = now }, cancellationToken: ct));
    }
}
