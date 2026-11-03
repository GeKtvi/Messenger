using Hangfire;
using Hangfire.Redis.StackExchange;

namespace Pingo.Identity.Api.Jobs;

// Background jobs run by Hangfire, with Redis as its job storage. Hangfire keeps the schedule and
// each run's state in Redis, so with several Identity instances a recurring job still runs once,
// and a run that was interrupted by a restart is picked up again.
public static class HangfireSetup
{
    public const string DeleteExpiredRefreshTokensJobId = "delete-expired-refresh-tokens";

    public static IServiceCollection AddPingoBackgroundJobs(this IServiceCollection services, string? redisConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redisConnectionString);

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // A prefix, so other services can share the same Redis without mixing their jobs.
            .UseRedisStorage(redisConnectionString, new RedisStorageOptions { Prefix = "pingo-identity:hangfire:" }));

        // The server is a hosted service that polls Redis and runs due jobs in this process.
        services.AddHangfireServer();
        return services;
    }

    // Registers (or updates) the recurring jobs. Safe to call on every start: AddOrUpdate keeps one
    // schedule per job id.
    public static void ScheduleRecurringJobs(this IApplicationBuilder app)
    {
        var jobs = app.ApplicationServices.GetRequiredService<IRecurringJobManager>();

        // Once a day at 03:00 UTC. Hangfire resolves IRefreshTokenCleanup from a new DI scope for
        // each run and passes its own cancellation token in place of CancellationToken.None.
        jobs.AddOrUpdate<IRefreshTokenCleanup>(
            DeleteExpiredRefreshTokensJobId,
            cleanup => cleanup.DeleteExpiredAsync(CancellationToken.None),
            Cron.Daily(3));
    }
}
