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
    ];
}
