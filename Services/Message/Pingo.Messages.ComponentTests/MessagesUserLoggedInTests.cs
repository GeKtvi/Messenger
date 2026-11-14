using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Pingo.Contracts;

namespace Pingo.Messages.ComponentTests;

// The UserLoggedIn event from Identity, delivered through MassTransit's in-memory test harness.
public class MessagesUserLoggedInTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private ITestHarness Harness => factory.Services.GetTestHarness();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UserLoggedIn_PostsAGreetingToTheChat()
    {
        var eventId = Guid.NewGuid();

        await Harness.Bus.Publish(new UserLoggedIn("ann@example.com"), context => context.MessageId = eventId);

        (await Harness.Consumed.Any<UserLoggedIn>(c => c.Context.MessageId == eventId)).Should().BeTrue();
        var greeting = (await _client.GetMessagesAsync()).Should().ContainSingle().Subject;
        greeting.Id.Should().Be(eventId);
        greeting.Text.Should().Be("Привет, ann@example.com!");
        greeting.SentAt.Should().Be(factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task SameEventDeliveredTwice_PostsOneGreeting()
    {
        // RabbitMQ delivers at least once; a redelivery has the same MessageId.
        var eventId = Guid.NewGuid();
        var loggedIn = new UserLoggedIn("ann@example.com");

        // The harness lists each MessageId only once, so count the finished deliveries directly.
        var deliveries = new FinishedDeliveries(eventId, expected: 2);
        using var observer = Harness.Bus.ConnectConsumeObserver(deliveries);

        await Harness.Bus.Publish(loggedIn, context => context.MessageId = eventId);
        await Harness.Bus.Publish(loggedIn, context => context.MessageId = eventId);

        await deliveries.AllFinished.WaitAsync(TimeSpan.FromSeconds(10));
        (await _client.GetMessagesAsync()).Should().ContainSingle(m => m.Id == eventId);
    }

    [Fact]
    public async Task TwoLogins_PostTwoGreetings()
    {
        // The harness remembers events from earlier tests too, so only these two count.
        string[] emails = ["carol@example.com", "dave@example.com"];
        foreach (var email in emails)
            await Harness.Bus.Publish(new UserLoggedIn(email));

        var consumed = Harness.Consumed.SelectAsync<UserLoggedIn>(c => emails.Contains(c.Context.Message.Email));
        (await AsyncElementListExtensions.Take(consumed, 2).CountAsync()).Should().Be(2);
        (await _client.GetMessagesAsync()).Select(m => m.Text).Should().BeEquivalentTo(
            "Привет, carol@example.com!", "Привет, dave@example.com!");
    }

    // Completes once the given number of deliveries of one event have been consumed successfully.
    private sealed class FinishedDeliveries(Guid messageId, int expected) : IConsumeObserver
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _finished;

        public Task AllFinished => _all.Task;

        public Task PostConsume<T>(ConsumeContext<T> context) where T : class
        {
            if (context.MessageId == messageId && Interlocked.Increment(ref _finished) == expected)
                _all.TrySetResult();
            return Task.CompletedTask;
        }

        public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;

        public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class =>
            Task.CompletedTask;
    }
}
