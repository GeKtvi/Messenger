namespace Pingo.Identity.Data;

internal record Migration(int Version, string Name, string Sql);

// The database schema, as hand-written SQL. Migrations run in version order, once each. An applied
// migration must never change: fix mistakes by adding a new one.
internal static class Migrations
{
    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, "CreateUsers", """
            CREATE TABLE users (
                id            uuid        PRIMARY KEY,
                registered_at timestamptz NOT NULL
            );
            """),

        // Email and password sign-in, kept apart from users so other ways to sign in (for example
        // OAuth) can get their own tables. One row per user; the email is stored normalized.
        new(2, "CreatePasswordLogins", """
            CREATE TABLE password_logins (
                user_id             uuid        PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
                email               text        NOT NULL UNIQUE,
                password_hash       bytea       NOT NULL,
                password_salt       bytea       NOT NULL,
                password_iterations integer     NOT NULL,
                created_at          timestamptz NOT NULL
            );
            """),

        // Refresh tokens are stored as SHA-256 hashes, never as the token itself.
        new(3, "CreateRefreshTokens", """
            CREATE TABLE refresh_tokens (
                token_hash bytea       PRIMARY KEY,
                user_id    uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL
            );

            CREATE INDEX refresh_tokens_user_id_idx ON refresh_tokens (user_id);
            """),
    ];
}
