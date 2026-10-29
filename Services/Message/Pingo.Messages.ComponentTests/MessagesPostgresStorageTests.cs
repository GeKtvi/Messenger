using System.Net;
using FluentAssertions;

namespace Pingo.Messages.ComponentTests;

// Storage details the in-memory database hides: PostgreSQL's time precision and column limits.
// Needs Docker. Skip with: dotnet test --filter "Category!=Docker"
[Trait("Category", "Docker")]
public class MessagesPostgresStorageTests(PostgresMessagesApiFactory factory)
    : IClassFixture<PostgresMessagesApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ClockWithSubMicrosecondTicks_ResponsesMatchStoredTimes()
    {
        // PostgreSQL keeps microseconds; the clock has 100 ns ticks. A real clock almost always
        // has a non-zero remainder, so a send must not return a time that a later read won't.
        var id = Guid.NewGuid();
        factory.Time.Advance(TimeSpan.FromTicks(1_234_567));

        var created = await (await _client.SendMessageAsync(id, "hello")).ReadMessageAsync();
        var retried = await (await _client.SendMessageAsync(id, "hello")).ReadMessageAsync();
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Should().Be(created);
        retried.Should().Be(created);

        factory.Time.Advance(TimeSpan.FromTicks(7_654_321));
        var edited = await (await _client.SendMessageAsync(id, "edited")).ReadMessageAsync();
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Should().Be(edited);
    }

    [Fact]
    public async Task TextAtMaxLength_FitsTheDatabaseColumn()
    {
        // Non-ASCII on purpose: PostgreSQL limits characters, not bytes.
        var text = new string('я', MessageLimits.MaxTextLength);

        var response = await _client.SendMessageAsync(Guid.NewGuid(), text);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Text.Should().Be(text);
    }
}
