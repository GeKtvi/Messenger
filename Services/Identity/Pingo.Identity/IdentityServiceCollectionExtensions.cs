using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingo.Identity.Data;

namespace Pingo.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Registers the Identity business layer: the PostgreSQL connection (used with Dapper), the
    // background service that applies the migrations on startup, and IAuthService.
    public static IServiceCollection AddPingoIdentity(this IServiceCollection services, string? connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new IdentityDatabase(connectionString));
        services.AddHostedService<DatabaseMigrator>();
        services.AddScoped<IAuthService, AuthService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
