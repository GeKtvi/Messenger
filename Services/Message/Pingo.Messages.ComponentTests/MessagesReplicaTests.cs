using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pingo.Messages.Data;

namespace Pingo.Messages.ComponentTests;

// A replica that never catches up: reads go to a second database that stays empty, so a test can tell
// which of the two answered.
public class LaggingReplicaApiFactory : MessagesApiFactory
{
    private readonly string _replicaName = $"replica-{Guid.NewGuid()}";

    protected override void ConfigureReadDatabase(DbContextOptionsBuilder options) =>
        options.UseInMemoryDatabase(_replicaName);
}

// Lists always come from the read replica, sends always go to the primary.
public class MessagesReplicaTests(LaggingReplicaApiFactory factory)
    : IClassFixture<LaggingReplicaApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ReadsTheReplica()
    {
        await _client.SendMessageAsync(Guid.NewGuid(), "only on the primary so far");

        (await _client.GetMessagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatedSend_FindsTheMessageOnThePrimary()
    {
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "hello");

        // The replica doesn't have the message, so looking there would miss it.
        var response = await _client.SendMessageAsync(id, "hello");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadOnlyContext_RefusesToSave()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReadOnlyMessageDbContext>();
        db.Add(new Entities.Message { Id = Guid.NewGuid(), Text = "hello" });

        var save = () => db.SaveChangesAsync();
        var saveSync = () => db.SaveChanges();

        await save.Should().ThrowAsync<InvalidOperationException>();
        saveSync.Should().Throw<InvalidOperationException>();
    }
}
