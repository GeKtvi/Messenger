using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingo.Identity.Data;
using Pingo.Identity.Passwords;
using Pingo.Identity.Tokens;

namespace Pingo.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Registers the Identity business layer: the PostgreSQL connection (used with Dapper), the
    // background service that applies the migrations on startup, the repositories, token issuing
    // and IAuthService.
    public static IServiceCollection AddPingoIdentity(
        this IServiceCollection services, string? connectionString, IConfigurationSection jwtSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(new IdentityDatabase(connectionString));
        services.AddHostedService<DatabaseMigrator>();

        // A missing or too short signing key, or a lifetime out of range, fails at startup, not on the
        // first login.
        services.AddOptions<JwtOptions>().Bind(jwtSection).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<TokenFactory>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // Repositories hold no state, only the shared IdentityDatabase, so one instance each is enough.
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IPasswordLoginRepository, PasswordLoginRepository>();
        services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IRefreshTokenCleanup, RefreshTokenCleanup>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
