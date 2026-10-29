using Microsoft.Extensions.DependencyInjection;

namespace Pingo.Identity;

public static class IdentityServiceCollectionExtensions
{
    // Registers the Identity business layer. Storage (Dapper + PostgreSQL), registration, login and
    // token issuing are added here in the next steps.
    public static IServiceCollection AddPingoIdentity(this IServiceCollection services) => services;
}
