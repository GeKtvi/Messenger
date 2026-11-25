using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pingo.Messages.Data;

namespace Pingo.Messages;

public static class MessagesServiceCollectionExtensions
{
    // readConnectionString points at a read replica. Without it, everything uses the primary.
    private const string Primary = "primary";
    private const string Replica = "replica";

    public static IServiceCollection AddMessages(
        this IServiceCollection services, string? connectionString, string? readConnectionString = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Data sources of our own rather than connection strings, so the retry strategy can reach them.
        services.AddKeyedSingleton(Primary, (_, _) => NpgsqlDataSource.Create(connectionString));
        services.AddKeyedSingleton(Replica, (_, _) => NpgsqlDataSource.Create(
            string.IsNullOrWhiteSpace(readConnectionString) ? connectionString : readConnectionString));
        services.AddDbContext<MessageDbContext>((provider, options) =>
            UseDatabase(options, provider.GetRequiredKeyedService<NpgsqlDataSource>(Primary)));
        services.AddDbContext<ReadOnlyMessageDbContext>((provider, options) =>
            UseDatabase(options, provider.GetRequiredKeyedService<NpgsqlDataSource>(Replica)));
        services.AddScoped<IMessageService, MessageService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    internal static DbContextOptionsBuilder UseDatabase(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options.UseNpgsql(dataSource, npgsql =>
            npgsql.ExecutionStrategy(dependencies => new RetryingExecutionStrategy(dependencies, dataSource)));

    // Applies pending EF Core migrations on startup. Non-relational providers (the in-memory
    // database used by tests) don't support migrations, so they get the schema created directly.
    public static async Task MigrateMessagesDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<MessageDbContext>().Database;

        if (database.IsRelational())
            await database.MigrateAsync();
        else
            await database.EnsureCreatedAsync();
    }
}
