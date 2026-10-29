using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pingo.Messages.Data;

namespace Pingo.Messages.ComponentTests;

// Concurrent sends against a real PostgreSQL. The in-memory provider can't be used here: the
// "same id inserted twice at once" case depends on PostgreSQL rejecting the duplicate key.
// Needs Docker. Skip with: dotnet test --filter "Category!=Docker"
[Trait("Category", "Docker")]
public class MessagesConcurrencyTests(PostgresMessagesApiFactory factory)
    : IClassFixture<PostgresMessagesApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConcurrentCreatesOfSameId_StoreOneMessageWithoutErrors()
    {
        const int requests = 10;
        var id = Guid.NewGuid();
        var gate = factory.InsertGate = new InsertGate(requests);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, requests).Select(_ => _client.SendMessageAsync(id, "hello")));

        gate.Arrived.Should().Be(requests, "every request must reach the insert at the same moment");
        responses.Select(r => r.StatusCode).Should()
            .OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.OK, "a duplicate insert must not be a 500")
            .And.ContainSingle(s => s == HttpStatusCode.Created, "exactly one request creates the message");

        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Should().Match<Api.Contracts.MessageResponse>(m => m.Id == id && m.Text == "hello" && m.UpdatedAt == null);
    }

    [Fact]
    public async Task ConcurrentCreatesOfSameId_WithDifferentTexts_KeepOneOfThem()
    {
        const int requests = 5;
        var id = Guid.NewGuid();
        var texts = Enumerable.Range(1, requests).Select(i => $"version {i}").ToList();
        factory.InsertGate = new InsertGate(requests);

        var responses = await Task.WhenAll(texts.Select(text => _client.SendMessageAsync(id, text)));

        responses.Select(r => r.StatusCode).Should()
            .OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.OK)
            .And.ContainSingle(s => s == HttpStatusCode.Created);

        // Last write wins: the stored text is whichever request saved last.
        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Text.Should().BeOneOf(texts);
    }

    [Fact]
    public async Task ConcurrentCreatesOfDifferentIds_StoreEveryMessage()
    {
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        var responses = await Task.WhenAll(ids.Select(id => _client.SendMessageAsync(id, $"message {id}")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await _client.GetMessagesAsync()).Select(m => m.Id).Should().BeEquivalentTo(ids);
    }

    [Fact]
    public async Task ConcurrentEditsOfSameMessage_KeepOneMessageWithOneOfTheEdits()
    {
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "original");
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        var edits = Enumerable.Range(1, 10).Select(i => $"edit {i}").ToList();

        var responses = await Task.WhenAll(edits.Select(text => _client.SendMessageAsync(id, text)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var message = (await _client.GetMessagesAsync()).Should().ContainSingle().Subject;
        message.Text.Should().BeOneOf(edits);
        message.UpdatedAt.Should().Be(factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task Startup_AppliesAllMigrations()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessageDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        pending.Should().BeEmpty();
        applied.Should().Equal(db.Database.GetMigrations());
    }
}
