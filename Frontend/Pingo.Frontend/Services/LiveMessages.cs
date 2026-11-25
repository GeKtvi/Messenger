using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Pingo.Frontend.Models;
using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Services;

// Messages saved by anyone, pushed by the Message API while the chat is open.
public interface ILiveMessages
{
    // A message was created or edited.
    event Action<MessageDto>? Received;

    // The connection was lost and is back: anything saved meanwhile was missed.
    event Action? Reconnected;

    // Connects in the background; keeps trying until StopAsync.
    void Start();

    Task StopAsync();
}

// SignalR over a WebSocket to the Message API's hub.
public sealed class LiveMessages(Uri hubUrl, IAuthSession session, ILogger<LiveMessages> logger) : ILiveMessages
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private HubConnection? _connection;
    private CancellationTokenSource? _running;

    public event Action<MessageDto>? Received;

    public event Action? Reconnected;

    public void Start()
    {
        if (_running is not null)
            return;

        var running = _running = new CancellationTokenSource();
        var connection = _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // A WebSocket straight away, without the negotiate request. That request and the
                // connection could otherwise reach different servers, which SignalR doesn't allow.
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.AccessTokenProvider = () => Task.FromResult(session.AccessToken);
            })
            // Retries after 0, 2, 10 and 30 seconds, then gives up and raises Closed.
            .WithAutomaticReconnect()
            .Build();
        connection.On<MessageDto>("MessageSaved", message => Received?.Invoke(message));
        connection.Reconnected += _ =>
        {
            Reconnected?.Invoke();
            return Task.CompletedTask;
        };
        connection.Closed += _ => ConnectAsync(connection, running.Token);

        _ = ConnectAsync(connection, running.Token);
    }

    public async Task StopAsync()
    {
        // Cleared first, so a Start right after this call connects anew.
        var (running, connection) = (_running, _connection);
        (_running, _connection) = (null, null);
        if (running is null || connection is null)
            return;

        await running.CancelAsync();
        await connection.DisposeAsync();
    }

    // Also after the automatic reconnects have given up. Each failed attempt gets a fresh access token
    // first: an expired one is the usual reason the hub turns a connection down.
    private async Task ConnectAsync(HubConnection connection, CancellationToken ct)
    {
        var first = true;
        while (true)
        {
            try
            {
                if (!first)
                    await session.RefreshAsync();
                first = false;

                await connection.StartAsync(ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "Live messages unavailable; trying again in {Delay}", RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
