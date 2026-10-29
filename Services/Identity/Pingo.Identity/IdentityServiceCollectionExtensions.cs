using Microsoft.Extensions.DependencyInjection;
using Pingo.Identity.Data;

namespace Pingo.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Registers the Identity business layer: the PostgreSQL connection (used with Dapper) and the
    // background service that applies the migrations on startup.
    public static IServiceCollection AddPingoIdentity(this IServiceCollection services, string? connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new IdentityDatabase(connectionString));
        services.AddHostedService<DatabaseMigrator>();

        return services;
    }
}
