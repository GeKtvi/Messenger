using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Pingo.Messages.ComponentTests;

// Fast default: the EF Core in-memory provider. Not a real database, so it can't test SQL
// constraints, PostgreSQL errors or migrations; PostgresMessagesApiFactory covers those.
public class MessagesApiFactory : MessagesApiFactoryBase
{
    // Unique per factory, so parallel test classes never share data.
    private readonly string _databaseName = $"messages-{Guid.NewGuid()}";

    protected override void ConfigureDatabase(DbContextOptionsBuilder options) =>
        options.UseInMemoryDatabase(_databaseName);

    public override async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = CreateDbContext(scope);
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }
}
