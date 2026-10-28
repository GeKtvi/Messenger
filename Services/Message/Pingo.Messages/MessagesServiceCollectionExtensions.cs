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

    // Simple for now: create the schema on startup. Switch to migrations once the model settles.
    public static async Task EnsureMessagesDatabaseCreatedAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MessageDbContext>().Database.EnsureCreatedAsync();
    }
}
