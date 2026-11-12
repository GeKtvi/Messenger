using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Services;

// How the Message API client retries temporary failures. The defaults are for the app; tests use
// shorter delays.
public class MessageApiRetryOptions
{
    public int MaxRetryAttempts { get; set; } = 3;

    // The first wait; each next one doubles, with some randomness (0.5 s, ~1 s, ~2 s).
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(500);

    // An attempt that takes longer than this is cancelled and retried.
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

public static class ApiClientsSetup
{
    // The HTTP clients and sign-in services. Program.cs and the tests both use this, so the tests run
    // the same handler pipeline as the app.
    public static IServiceCollection AddPingoApiClients(this IServiceCollection services, Uri messageApi, Uri identityApi)
    {
        // Sign-in: the session holds the access token in memory; Identity keeps the refresh token in
        // an HttpOnly cookie (D-054). No retries here: refreshing twice after a lost response would
        // spend the single-use cookie twice and sign the user out.
        services.AddHttpClient<IdentityApiClient>(client => client.BaseAddress = identityApi);
        services.AddSingleton<IAuthSession, AuthSession>();
        services.AddSingleton<AuthenticationStateProvider, PingoAuthenticationStateProvider>();

        services.AddOptions<MessageApiRetryOptions>();
        services.AddTransient<AccessTokenHandler>();

        // Handlers run outside in, in the order added:
        // 1. AccessTokenHandler adds the bearer token and handles 401 by refreshing (D-056);
        // 2. the resilience handler retries temporary failures: 5xx, 408, 429, network errors and
        //    attempts that time out. Both calls are safe to repeat: GET, and PUT with the client's
        //    message id (D-023).
        services.AddHttpClient<MessageApiClient>(client => client.BaseAddress = messageApi)
            .AddHttpMessageHandler<AccessTokenHandler>()
            .AddResilienceHandler("message-api", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<MessageApiRetryOptions>>().Value;
                pipeline
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = options.MaxRetryAttempts,
                        Delay = options.Delay,
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true,
                    })
                    // Inside the retry, so it limits each attempt rather than all of them together.
                    .AddTimeout(options.AttemptTimeout);
            });

        return services;
    }
}
