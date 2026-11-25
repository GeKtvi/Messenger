using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pingo.Frontend.Models;
using Pingo.Frontend.Services;

namespace Pingo.Frontend.Tests;

// The outbox against a fake Message API and a fake localStorage. A new outbox on the same storage is
// what a page reload does.
public class MessageOutboxTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLocalStorage _storage = new();
    private readonly FakeApi _api = new();

    [Fact]
    public async Task DeliveredMessage_IsShown_UntilAListContainsIt()
    {
        var outbox = await OpenAsync();
        await SendAsync(outbox, "hello");

        outbox.Pending.Should().BeEmpty();
        var shown = outbox.Merge([]).Should().ContainSingle(m => m.Text == "hello").Subject;

        await outbox.ForgetListedAsync([shown]);
        outbox.Merge([]).Should().BeEmpty();
    }

    [Fact]
    public async Task DeliveredMessage_SurvivesAReload_AndIsNotSentAgain()
    {
        await SendAsync(await OpenAsync(), "hello");

        var reloaded = await OpenAsync();

        reloaded.Merge([]).Should().ContainSingle(m => m.Text == "hello" && m.SentAt == Start);
        reloaded.Pending.Should().BeEmpty();
        _api.Sends.Should().Be(1);
    }

    [Fact]
    public async Task ForgottenMessage_DoesNotComeBackAfterAReload()
    {
        var outbox = await OpenAsync();
        await SendAsync(outbox, "hello");
        await outbox.ForgetListedAsync(outbox.Merge([]));

        (await OpenAsync()).Merge([]).Should().BeEmpty();
    }

    [Fact]
    public async Task ListWithoutTheMessage_DoesNotForgetIt()
    {
        var outbox = await OpenAsync();
        await SendAsync(outbox, "hello");

        await outbox.ForgetListedAsync([Message("someone else's", 0)]);

        outbox.Merge([]).Should().ContainSingle(m => m.Text == "hello");
    }

    [Fact]
    public async Task Merge_SortsByTime_AndShowsTheListsCopyOnce()
    {
        var outbox = await OpenAsync();
        await SendAsync(outbox, "mine, listed");
        var listed = outbox.Merge([]).Single() with { Text = "the list's copy" };
        _api.Now = Start.AddMinutes(1);
        await SendAsync(outbox, "mine, not listed");

        var shown = outbox.Merge([Message("older", -1), listed, Message("newer", 2)]);

        shown.Select(m => m.Text).Should().Equal("older", "the list's copy", "mine, not listed", "newer");
    }

    [Fact]
    public async Task OldDeliveredMessage_IsKept()
    {
        _api.Now = DateTimeOffset.UnixEpoch;
        await SendAsync(await OpenAsync(), "hello");

        (await OpenAsync()).Merge([]).Should().ContainSingle(m => m.Text == "hello");
    }

    [Fact]
    public async Task FailedMessage_IsSentAgainOnAReload_WithTheSameId()
    {
        _api.Down = true;
        var outbox = await OpenAsync();
        await outbox.SendAsync("hello");
        await WaitAsync(() => outbox.Pending.Single().Status == PendingStatus.Failed);
        var id = outbox.Pending.Single().Id;

        _api.Down = false;
        var reloaded = await OpenAsync();
        await WaitAsync(() => reloaded.Pending.Count == 0);

        reloaded.Merge([]).Should().ContainSingle(m => m.Id == id);
    }

    [Fact]
    public async Task OutboxSavedByAnOlderVersion_IsStillSent()
    {
        var id = Guid.NewGuid();
        _storage.Value = $$"""[{"Id":"{{id}}","Text":"old","CreatedAt":"2026-01-01T11:59:00+00:00"}]""";

        var outbox = await OpenAsync();
        await WaitAsync(() => outbox.Pending.Count == 0);

        outbox.Merge([]).Should().ContainSingle(m => m.Id == id && m.Text == "old");
    }

    [Fact]
    public async Task Logout_ForgetsDeliveredMessages()
    {
        var outbox = await OpenAsync();
        await SendAsync(outbox, "hello");

        await outbox.ClearAsync();

        outbox.Merge([]).Should().BeEmpty();
        (await OpenAsync()).Merge([]).Should().BeEmpty();
    }

    [Fact]
    public async Task LogoutDuringASend_DoesNotKeepTheMessageForTheNextUser()
    {
        _api.Gate = new TaskCompletionSource();
        var outbox = await OpenAsync();
        await outbox.SendAsync("hello");
        await outbox.ClearAsync();
        var finished = new TaskCompletionSource();
        outbox.Changed += () => finished.TrySetResult();

        _api.Gate.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        outbox.Merge([]).Should().BeEmpty();
        _storage.Value.Should().Be("[]");
    }

    private async Task<MessageOutbox> OpenAsync()
    {
        var api = new MessageApiClient(new HttpClient(_api) { BaseAddress = new Uri("http://messages.test/") });
        var outbox = new MessageOutbox(api, _storage);
        await outbox.InitializeAsync();
        return outbox;
    }

    private static async Task SendAsync(MessageOutbox outbox, string text)
    {
        await outbox.SendAsync(text);
        await WaitAsync(() => outbox.Pending.Count == 0);
    }

    private static MessageDto Message(string text, int minute) =>
        new(Guid.NewGuid(), text, Start.AddMinutes(minute), null);

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline);
            await Task.Delay(10);
        }
    }

    // Accepts every message, as the real API does on PUT.
    private sealed class FakeApi : HttpMessageHandler
    {
        // The server's clock.
        public DateTimeOffset Now { get; set; } = Start;

        public bool Down { get; set; }

        public int Sends { get; private set; }

        // When set, requests wait for it.
        public TaskCompletionSource? Gate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Gate is not null)
                await Gate.Task.WaitAsync(ct);
            if (Down)
                throw new HttpRequestException("Connection refused");

            Sends++;
            var body = await request.Content!.ReadFromJsonAsync<SendMessageRequest>(ct);
            var message = new MessageDto(Guid.Parse(request.RequestUri!.Segments[^1]), body!.Text, Now, null);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(message) };
        }
    }
}
