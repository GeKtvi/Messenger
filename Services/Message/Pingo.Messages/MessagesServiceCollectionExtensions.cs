using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingo.Messages.Data;

namespace Pingo.Messages;

public static class MessagesServiceCollectionExtensions
{
    public static IServiceCollection AddMessages(this IServiceCollection services, string? connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MessageDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IMessageService, MessageService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

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
