using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pingo.Edge.Tests;

// A stand-in for one API in one region, on a real local port. It can be stopped and started again.
public sealed class FakeApi : IAsyncDisposable
{
    private WebApplication? _app;

    public int Port { get; } = FreePort();

    public string Address => $"http://127.0.0.1:{Port}";

    // What /health answers.
    public int HealthStatus { get; set; } = StatusCodes.Status200OK;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(Address);
        var app = builder.Build();
        app.MapGet("/health", () => Results.StatusCode(HealthStatus));
        app.MapGet("/{**path}", () => Results.Ok());
        await app.StartAsync();
        _app = app;
    }

    public async Task StopAsync()
    {
        if (_app is null)
            return;
        await _app.DisposeAsync();
        _app = null;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
