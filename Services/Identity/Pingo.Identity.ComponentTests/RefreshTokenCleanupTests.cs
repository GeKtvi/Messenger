using System.Net;
using Dapper;
using FluentAssertions;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Pingo.Identity.Api.Jobs;

namespace Pingo.Identity.ComponentTests;

// The daily Hangfire job that deletes expired refresh tokens, and the index it relies on.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class RefreshTokenCleanupTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    [Fact]
    public async Task Cleanup_DeletesExpiredTokens_AndKeepsTheOthersWorking()
    {
        var expired = await Client.RegisterAndLoginAsync("ann@example.com");
        Factory.Time.Advance(TimeSpan.FromDays(10));
        var valid = await Client.RegisterAndLoginAsync("bob@example.com");
        Factory.Time.Advance(TimeSpan.FromDays(21));

        var deleted = await RunCleanupAsync();

        deleted.Should().Be(1);
        (await CountTokensAsync()).Should().Be(1);
        (await Client.RefreshAsync(valid.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.RefreshAsync(expired.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cleanup_TokenExpiringExactlyNow_IsKeptUntilTheNextRun()
    {
        await Client.RegisterAndLoginAsync("ann@example.com");
        Factory.Time.Advance(TimeSpan.FromDays(30));

        var deleted = await RunCleanupAsync();

        deleted.Should().Be(0);
        (await CountTokensAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_IsScheduledWithHangfireOnceADay()
    {
        using var connection = Factory.Services.GetRequiredService<JobStorage>().GetConnection();

        var job = connection.GetRecurringJobs().Should()
            .ContainSingle(j => j.Id == HangfireSetup.DeleteExpiredRefreshTokensJobId).Subject;

        job.Cron.Should().Be(Cron.Daily(3));
        job.Job.Type.Should().Be(typeof(IRefreshTokenCleanup));
        job.Job.Method.Name.Should().Be(nameof(IRefreshTokenCleanup.DeleteExpiredAsync));
    }

    [Fact]
    public async Task Cleanup_TriggeredThroughHangfire_DeletesExpiredTokens()
    {
        // The whole path: Redis storage, the Hangfire server in the app, DI and the job itself.
        await Client.RegisterAndLoginAsync("ann@example.com");
        Factory.Time.Advance(TimeSpan.FromDays(31));

        Factory.Services.GetRequiredService<IRecurringJobManager>().Trigger(HangfireSetup.DeleteExpiredRefreshTokensJobId);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await CountTokensAsync() > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(200);
        (await CountTokensAsync()).Should().Be(0, "the triggered job should have run within 30 seconds");
    }

    [Fact]
    public async Task Migrations_CreateAnIndexOnExpiresAt()
    {
        await using var db = await Factory.OpenConnectionAsync();

        var definition = await db.ExecuteScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'refresh_tokens_expires_at_idx'");

        definition.Should().Contain("ON public.refresh_tokens").And.Contain("(expires_at)");
    }

    private async Task<int> RunCleanupAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRefreshTokenCleanup>().DeleteExpiredAsync();
    }

    private async Task<long> CountTokensAsync()
    {
        await using var db = await Factory.OpenConnectionAsync();
        return await db.ExecuteScalarAsync<long>("SELECT count(*) FROM refresh_tokens");
    }
}
