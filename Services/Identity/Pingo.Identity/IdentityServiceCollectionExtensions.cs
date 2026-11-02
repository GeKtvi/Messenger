using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingo.Identity.Data;
using Pingo.Identity.Tokens;

namespace Pingo.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Registers the Identity business layer: the PostgreSQL connection (used with Dapper), the
    // background service that applies the migrations on startup, token issuing and IAuthService.
    public static IServiceCollection AddPingoIdentity(
        this IServiceCollection services, string? connectionString, IConfigurationSection jwtSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new IdentityDatabase(connectionString));
        services.AddHostedService<DatabaseMigrator>();

        // A missing or too short signing key fails at startup, not on the first login.
        services.AddOptions<JwtOptions>().Bind(jwtSection).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<TokenFactory>();

        services.AddScoped<IAuthService, AuthService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
