using System.Net;
using FluentAssertions;
using Pingo.Messages.Api.Contracts;

namespace Pingo.Messages.ComponentTests;

public class MessagesApiTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    // Runs before every test: same app, empty database.
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetMessages_WhenNothingWasSent_ReturnsEmptyList()
    {
        var messages = await _client.GetMessagesAsync();

        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessage_NewId_MessageAppearsInList()
    {
        var id = Guid.NewGuid();
        var sentAt = factory.Time.GetUtcNow();

        var response = await _client.SendMessageAsync(id, "Hello, Pingo!");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().Be($"/api/messages/{id}");

        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new MessageResponse(id, "Hello, Pingo!", sentAt, UpdatedAt: null));
    }

    [Fact]
    public async Task SendMessage_TwoMessages_ListedInSendOrder()
    {
        // The second id sorts before the first, so the list can't pass by being ordered by id.
        var firstId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        await _client.SendMessageAsync(firstId, "first");
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _client.SendMessageAsync(secondId, "second");

        var messages = await _client.GetMessagesAsync();

        messages.Select(m => m.Text).Should().Equal("first", "second");
        messages.Select(m => m.Id).Should().Equal(firstId, secondId);
    }

    [Fact]
    public async Task SendMessage_SameIdWithNewText_UpdatesTextAndEditTime()
    {
        var id = Guid.NewGuid();
        var sentAt = factory.Time.GetUtcNow();
        await _client.SendMessageAsync(id, "original");

        factory.Time.Advance(TimeSpan.FromMinutes(5));
        var editedAt = factory.Time.GetUtcNow();
        var response = await _client.SendMessageAsync(id, "edited");

        var expected = new MessageResponse(id, "edited", sentAt, editedAt);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ReadMessageAsync()).Should().Be(expected);

        var messages = await _client.GetMessagesAsync();
        messages.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public async Task EditingOlderMessage_DoesNotChangeOrder()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await _client.SendMessageAsync(firstId, "first");
        factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _client.SendMessageAsync(secondId, "second");

        factory.Time.Advance(TimeSpan.FromSeconds(1));
        await _client.SendMessageAsync(firstId, "first, edited");

        var messages = await _client.GetMessagesAsync();
        messages.Select(m => m.Id).Should().Equal(firstId, secondId);
    }

    [Fact]
    public async Task MessagesWithSameSentAt_AreOrderedById()
    {
        // Sent in reverse id order without moving the clock. The ids differ only in the last byte,
        // so .NET and PostgreSQL agree on their order.
        var lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        await _client.SendMessageAsync(higherId, "higher");
        await _client.SendMessageAsync(lowerId, "lower");

        var messages = await _client.GetMessagesAsync();
        messages.Select(m => m.Id).Should().Equal(lowerId, higherId);
    }

    [Fact]
    public async Task SendMessage_NewIdWithSurroundingSpaces_StoresTrimmedText()
    {
        var id = Guid.NewGuid();

        var response = await _client.SendMessageAsync(id, "  hello  ");

        (await response.ReadMessageAsync()).Text.Should().Be("hello");
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Text.Should().Be("hello");
    }

    [Fact]
    public async Task SendMessage_EditWithSurroundingSpaces_StoresTrimmedText()
    {
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "original");

        var response = await _client.SendMessageAsync(id, "  edited  ");

        (await response.ReadMessageAsync()).Text.Should().Be("edited");
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Text.Should().Be("edited");
    }

    [Fact]
    public async Task SendMessage_TextAtMaxLength_IsCreated()
    {
        var text = new string('a', MessageLimits.MaxTextLength);

        var response = await _client.SendMessageAsync(Guid.NewGuid(), text);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Text.Should().Be(text);
    }

    [Fact]
    public async Task SendMessage_TextOverMaxLength_IsRejectedAndNotStored()
    {
        var text = new string('a', MessageLimits.MaxTextLength + 1);

        var response = await _client.SendMessageAsync(Guid.NewGuid(), text);

        var errors = await response.ReadValidationErrorsAsync();
        errors.Should().ContainKey("Text");
        (await _client.GetMessagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessage_TextAtMaxLengthPlusSurroundingSpaces_IsCreatedTrimmed()
    {
        // The limit applies to the stored text, which is trimmed.
        var text = new string('a', MessageLimits.MaxTextLength);

        var response = await _client.SendMessageAsync(Guid.NewGuid(), $"  {text}  ");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.GetMessagesAsync()).Should().ContainSingle().Which.Text.Should().Be(text);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "text": null }""")]
    [InlineData("""{ "text": "" }""")]
    [InlineData("""{ "text": "   " }""")]
    public async Task SendMessage_MissingNullOrBlankText_ReturnsRequiredErrorAndStoresNothing(string json)
    {
        var response = await _client.SendMessageJsonAsync(Guid.NewGuid(), json);

        var errors = await response.ReadValidationErrorsAsync();
        errors.Should().ContainKey("Text").WhoseValue.Should().Equal("Message text is required.");
        (await _client.GetMessagesAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "text": """)]
    [InlineData("""not json""")]
    [InlineData("")]
    public async Task SendMessage_MalformedBody_ReturnsBadRequest(string body)
    {
        var response = await _client.SendMessageJsonAsync(Guid.NewGuid(), body);

        await response.ReadValidationErrorsAsync();
        (await _client.GetMessagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessage_EmptyGuid_ReturnsValidationProblemAndStoresNothing()
    {
        var response = await _client.SendMessageAsync(Guid.Empty, "hello");

        var errors = await response.ReadValidationErrorsAsync();
        errors.Should().ContainKey("messageId").WhoseValue.Should().Equal("Message id must not be empty.");
        (await _client.GetMessagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessage_IdThatIsNotAGuid_ReturnsNotFound()
    {
        var response = await _client.PutAsync("/api/messages/42", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
