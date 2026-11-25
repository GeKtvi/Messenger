using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pingo.Frontend.Models;
using Pingo.Frontend.Services;

namespace Pingo.Frontend.Tests;

// The feed and the real outbox against a fake Message API, whose list each test sets.
public class MessageFeedTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeApi _api = new();
    private readonly FakeLiveMessages _live = new();
    private readonly MessageOutbox _outbox;
    private readonly MessageFeed _feed;

    public MessageFeedTests()
    {
        var client = new MessageApiClient(new HttpClient(_api) { BaseAddress = new Uri("http://messages.test/") });
        _outbox = new MessageOutbox(client, new FakeLocalStorage());
        _feed = new MessageFeed(client, _outbox, _live);
    }

    [Fact]
    public async Task Open_ShowsTheList()
    {
        _feed.Messages.Should().BeNull("nothing has been loaded yet");
        _api.List = [Message("one", 1), Message("two", 2)];

        await _feed.OpenAsync();

        Texts().Should().Equal("one", "two");
    }

    [Fact]
    public async Task Send_LoadsTheListAgain_AndShowsOthersNewMessages()
    {
        await _feed.OpenAsync();
        _api.List = [Message("someone else's", 1)];

        await SendAsync("mine");

        _api.Loads.Should().Be(2);
        Texts().Should().BeEquivalentTo("someone else's", "mine");
    }

    [Fact]
    public async Task Send_ShowsTheMessageBeforeTheListIsLoaded()
    {
        await _feed.OpenAsync();
        var gate = _api.HoldNextLoad();

        await _outbox.SendAsync("mine");
        await WaitAsync(() => _api.Loads == 2);

        Texts().Should().Equal("mine");
        _feed.Pending.Should().BeEmpty();
        gate.SetResult();
    }

    [Fact]
    public async Task Send_ListContainsTheMessage_ShowsItOnce()
    {
        await _feed.OpenAsync();
        _api.ListAcceptedMessages = true;

        await SendAsync("mine");

        Texts().Should().Equal("mine");
        _outbox.Merge([]).Should().BeEmpty("the list has it, so the outbox doesn't keep it any longer");
    }

    [Fact]
    public async Task FailedReload_KeepsTheList()
    {
        _api.List = [Message("one", 1)];
        await _feed.OpenAsync();
        _api.LoadsFail = true;

        await SendAsync("mine");

        Texts().Should().BeEquivalentTo("one", "mine");
        _feed.LoadError.Should().NotBeNull();
    }

    [Fact]
    public async Task FailedFirstLoad_ShowsAnEmptyChat_AndTheNextLoadFillsIt()
    {
        _api.LoadsFail = true;
        await _feed.OpenAsync();
        Texts().Should().BeEmpty();
        _feed.LoadError.Should().NotBeNull();

        _api.LoadsFail = false;
        _api.List = [Message("one", 1)];
        await _feed.LoadAsync();

        Texts().Should().Equal("one");
        _feed.LoadError.Should().BeNull();
    }

    [Fact]
    public async Task SendsDuringALoad_CauseExactlyOneMoreLoad()
    {
        await _feed.OpenAsync();
        var gate = _api.HoldNextLoad();
        var load = _feed.LoadAsync();
        await WaitAsync(() => _api.Loads == 2);

        for (var i = 0; i < 3; i++)
            await _outbox.SendAsync($"message {i}");
        await WaitAsync(() => _outbox.Pending.Count == 0);
        _api.Loads.Should().Be(2, "the second load is still running");

        gate.SetResult();
        await load;

        _api.Loads.Should().Be(3);
        _api.MostLoadsAtOnce.Should().Be(1);
    }

    [Fact]
    public async Task OlderList_DoesNotRemoveMessages()
    {
        _api.List = [Message("one", 1), Message("two", 2), Message("four", 4)];
        await _feed.OpenAsync();

        _api.List = [_api.List[0], Message("three", 3)];
        await _feed.LoadAsync();

        Texts().Should().Equal("one", "two", "three", "four");
    }

    [Fact]
    public async Task OlderList_DoesNotUndoAnEdit()
    {
        var original = Message("before", 1);
        _api.List = [original with { Text = "after", UpdatedAt = Start.AddMinutes(5) }];
        await _feed.OpenAsync();

        _api.List = [original];
        await _feed.LoadAsync();

        Texts().Should().Equal("after");
    }

    [Fact]
    public async Task NewerList_ShowsAnEdit()
    {
        var original = Message("before", 1);
        _api.List = [original];
        await _feed.OpenAsync();

        _api.List = [original with { Text = "after", UpdatedAt = Start.AddMinutes(5) }];
        await _feed.LoadAsync();

        Texts().Should().Equal("after");
    }

    [Fact]
    public async Task Reopened_StartsFromAFreshLoad()
    {
        _api.List = [Message("the previous user's view", 1)];
        await _feed.OpenAsync();
        _feed.Close();
        _feed.Messages.Should().BeNull();

        _api.List = [Message("fresh", 2)];
        await _feed.OpenAsync();

        Texts().Should().Equal("fresh");
    }

    [Fact]
    public async Task AnswerToALoadFromBeforeClosing_IsIgnored()
    {
        _api.List = [Message("the previous user's view", 1)];
        var gate = _api.HoldNextLoad();
        var firstOpen = _feed.OpenAsync();
        await WaitAsync(() => _api.Loads == 1);
        _feed.Close();

        _api.List = [Message("fresh", 2)];
        var secondOpen = _feed.OpenAsync();
        gate.SetResult();
        await Task.WhenAll(firstOpen, secondOpen);

        Texts().Should().Equal("fresh");
    }

    [Fact]
    public async Task SendFinishingAfterClosing_DoesNotLoad()
    {
        await _feed.OpenAsync();
        _feed.Close();

        await SendAsync("mine");

        _api.Loads.Should().Be(1);
        _feed.Messages.Should().BeNull();
    }

    [Fact]
    public async Task PushedMessage_IsShown()
    {
        _api.List = [Message("one", 1)];
        await _feed.OpenAsync();

        _live.Push(Message("someone else's", 2));

        Texts().Should().Equal("one", "someone else's");
        _api.Loads.Should().Be(1, "a push needs no load");
    }

    [Fact]
    public async Task PushedEdit_ReplacesTheText_AndAnOlderListDoesNotUndoIt()
    {
        var original = Message("before", 1);
        _api.List = [original];
        await _feed.OpenAsync();

        _live.Push(original with { Text = "after", UpdatedAt = Start.AddMinutes(5) });
        await _feed.LoadAsync();

        Texts().Should().Equal("after");
    }

    [Fact]
    public async Task MessagePushedDuringTheFirstLoad_IsShownAfterIt()
    {
        var gate = _api.HoldNextLoad();
        var open = _feed.OpenAsync();
        await WaitAsync(() => _api.Loads == 1);

        _live.Push(Message("pushed early", 2));
        _feed.Messages.Should().BeNull("the first load hasn't finished");
        gate.SetResult();
        await open;

        Texts().Should().Equal("pushed early");
    }

    [Fact]
    public async Task Reconnected_LoadsTheListAgain()
    {
        await _feed.OpenAsync();
        _api.List = [Message("missed while offline", 1)];

        _live.RaiseReconnected();
        await WaitAsync(() => _api.Loads == 2 && _api.LoadsRunning == 0);
        await Task.Delay(50);

        Texts().Should().Equal("missed while offline");
    }

    [Fact]
    public async Task LiveConnection_RunsOnlyWhileTheChatIsOpen()
    {
        await _feed.OpenAsync();
        _live.Running.Should().BeTrue();

        _feed.Close();
        _live.Push(Message("after closing", 1));

        _live.Running.Should().BeFalse();
        _feed.Messages.Should().BeNull();
    }

    private IEnumerable<string> Texts() => _feed.Messages!.Select(m => m.Text);

    // Sends and waits until it's delivered and the load it causes has finished.
    private async Task SendAsync(string text)
    {
        await _outbox.SendAsync(text);
        await WaitAsync(() => _outbox.Pending.Count == 0);
        // Long enough for the load to start; it runs against an in-memory fake.
        await Task.Delay(50);
        await WaitAsync(() => _api.LoadsRunning == 0);
        await Task.Delay(50);
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

    private sealed class FakeLiveMessages : ILiveMessages
    {
        public event Action<MessageDto>? Received;

        public event Action? Reconnected;

        public bool Running { get; private set; }

        public void Start() => Running = true;

        public Task StopAsync()
        {
            Running = false;
            return Task.CompletedTask;
        }

        public void Push(MessageDto message) => Received?.Invoke(message);

        public void RaiseReconnected() => Reconnected?.Invoke();
    }

    private sealed class FakeApi : HttpMessageHandler
    {
        private TaskCompletionSource? _gate;
        private int _loads, _running;

        public IReadOnlyList<MessageDto> List { get; set; } = [];

        public bool LoadsFail { get; set; }

        // Whether a sent message is in the next list, as with a replica that is up to date.
        public bool ListAcceptedMessages { get; set; }

        public int Loads => Volatile.Read(ref _loads);

        public int LoadsRunning => Volatile.Read(ref _running);

        public int MostLoadsAtOnce { get; private set; }

        public TaskCompletionSource HoldNextLoad() =>
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return await ListAsync(ct);

            var body = await request.Content!.ReadFromJsonAsync<SendMessageRequest>(ct);
            var message = new MessageDto(Guid.Parse(request.RequestUri!.Segments[^1]), body!.Text, Start.AddHours(1), null);
            if (ListAcceptedMessages)
                List = [.. List, message];
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(message) };
        }

        private async Task<HttpResponseMessage> ListAsync(CancellationToken ct)
        {
            // The answer is what the list was when the request arrived, even if it's held back.
            var list = List;
            var gate = Interlocked.Exchange(ref _gate, null);
            MostLoadsAtOnce = Math.Max(MostLoadsAtOnce, Interlocked.Increment(ref _running));
            Interlocked.Increment(ref _loads);
            try
            {
                if (gate is not null)
                    await gate.Task.WaitAsync(ct);
                return LoadsFail
                    ? throw new HttpRequestException("Connection refused")
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(list) };
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
