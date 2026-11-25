using System.Net;
using System.Threading.Channels;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Pingo.Contracts;
using Pingo.Messages.Api.Contracts;
using Pingo.Messages.Api.Live;

namespace Pingo.Messages.ComponentTests;

// Saved messages pushed to open chats over SignalR. The event travels through MassTransit's
// in-memory test harness, as it would through RabbitMQ.
public class MessagesLiveTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly Channel<MessageResponse> _received = Channel.CreateUnbounded<MessageResponse>();
    private HubConnection _hub = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _hub = Connect(TestAccessTokens.Create());
        _hub.On<MessageResponse>(MessagesHub.MessageSaved, message => _received.Writer.TryWrite(message));
        await _hub.StartAsync();
    }

    public async Task DisposeAsync() => await _hub.DisposeAsync();

    [Fact]
    public async Task Send_IsPushedToOpenChats()
    {
        var id = Guid.NewGuid();

        var sent = await (await _client.SendMessageAsync(id, "hello")).ReadMessageAsync();

        (await NextAsync()).Should().Be(sent);
    }

    [Fact]
    public async Task Edit_IsPushed_RepeatIsNot()
    {
        var id = Guid.NewGuid();
        await _client.SendMessageAsync(id, "before");
        await NextAsync();

        await _client.SendMessageAsync(id, "before");
        var edited = await (await _client.SendMessageAsync(id, "after")).ReadMessageAsync();

        // The repeat changed nothing, so the next push is the edit.
        (await NextAsync()).Should().Be(edited);
        edited.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Greeting_IsPushed()
    {
        var eventId = Guid.NewGuid();

        await factory.Services.GetTestHarness().Bus.Publish(
            new UserLoggedIn("ann@example.com"), context => context.MessageId = eventId);

        var pushed = await NextAsync();
        pushed.Id.Should().Be(eventId);
        pushed.Text.Should().Be("Привет, ann@example.com!");
    }

    [Fact]
    public async Task Connecting_WithoutAToken_IsRejected()
    {
        await using var hub = Connect(token: null);

        var connect = () => hub.StartAsync();

        (await connect.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WebSocket_WithTheTokenInTheQueryString_Connects()
    {
        // As browsers do: they can't set headers on a WebSocket.
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, $"{MessagesHub.Path.TrimStart('/')}?access_token={TestAccessTokens.Create()}"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = (context, ct) => new ValueTask<System.Net.WebSockets.WebSocket>(
                    factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, ct));
            })
            .Build();
        var received = new TaskCompletionSource<MessageResponse>();
        hub.On<MessageResponse>(MessagesHub.MessageSaved, message => received.TrySetResult(message));

        await hub.StartAsync();
        await _client.SendMessageAsync(Guid.NewGuid(), "hello");

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(10))).Text.Should().Be("hello");
    }

    [Fact]
    public async Task TokenInTheQueryString_IsNotAcceptedOutsideTheHub()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync($"/api/messages?access_token={TestAccessTokens.Create()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Long polling through the test server; it sends the token in the Authorization header.
    private HubConnection Connect(string? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, MessagesHub.Path.TrimStart('/')), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .Build();

    private async Task<MessageResponse> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _received.Reader.ReadAsync(timeout.Token);
    }
}
