using System.Net;
using FluentAssertions;
using Pingo.Messages.Api.Contracts;

namespace Pingo.Messages.ComponentTests;

// PUT /api/messages/{id} must be safe to retry: sending the same request again never creates a
// duplicate and never counts as an edit.
public class MessagesIdempotencyTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SameRequestRepeated_StoresOneMessageAndReturnsSameData()
    {
        var id = Guid.NewGuid();
        var sentAt = factory.Time.GetUtcNow();

        var first = await _client.SendMessageAsync(id, "hello");
        var original = await first.ReadMessageAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            factory.Time.Advance(TimeSpan.FromMinutes(1));
            var retry = await _client.SendMessageAsync(id, "hello");

            retry.StatusCode.Should().Be(HttpStatusCode.OK);
            (await retry.ReadMessageAsync()).Should().Be(original, "a retry returns the stored message unchanged");
        }

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Should().Be(new MessageResponse(id, "hello", sentAt, UpdatedAt: null));
    }

    [Fact]
    public async Task RepeatingAnEdit_KeepsTheFirstEditTime()
    {
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "original");

        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var editedAt = factory.Time.GetUtcNow();
        await _client.SendMessageAsync(id, "edited");

        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var retry = await _client.SendMessageAsync(id, "edited");

        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.UpdatedAt.Should().Be(editedAt, "retrying the edit is not a second edit");
    }

    [Fact]
    public async Task SeveralEdits_UpdatedAtIsLatestEdit_SentAtUnchanged()
    {
        var id = Guid.NewGuid();
        var sentAt = factory.Time.GetUtcNow();
        await _client.SendMessageAsync(id, "v1");

        foreach (var text in new[] { "v2", "v3", "v4" })
        {
            factory.Time.Advance(TimeSpan.FromMinutes(1));
            await _client.SendMessageAsync(id, text);
        }

        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Should().Be(new MessageResponse(id, "v4", sentAt, factory.Time.GetUtcNow()));
    }

    [Fact]
    public async Task TextDifferingOnlyBySurroundingSpaces_IsTheSameRequest()
    {
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "hello");

        factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _client.SendMessageAsync(id, "  hello  ");

        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Should().Match<MessageResponse>(m => m.Text == "hello" && m.UpdatedAt == null);
    }

    [Fact]
    public async Task StaleRetryAfterAnEdit_RevertsTheText()
    {
        // Documents a known limit of plain PUT: an old request arriving after a newer edit wins.
        // Preventing this needs a version check (for example ETag / If-Match).
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "v1");
        factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _client.SendMessageAsync(id, "v2");

        factory.Time.Advance(TimeSpan.FromMinutes(1));
        var revertedAt = factory.Time.GetUtcNow();
        await _client.SendMessageAsync(id, "v1");

        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Should().Match<MessageResponse>(m => m.Text == "v1" && m.UpdatedAt == revertedAt);
    }
}
