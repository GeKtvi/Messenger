using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Pingo.Edge.Tests;

// The real edge with its real configuration, in front of fake APIs in two regions. It prefers the US.
public sealed class FailoverTests : IAsyncLifetime
{
    private const string Messages = "/api/messages";
    private const string Identity = "/api/auth/login";

    private readonly FakeApi _messagesUs = new(), _messagesEu = new(), _identityUs = new(), _identityEu = new();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private FakeApi[] All => [_messagesUs, _messagesEu, _identityUs, _identityEu];

    public async Task InitializeAsync()
    {
        foreach (var api in All)
            await api.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Edge:RegionOrder", "us,eu");
            foreach (var (cluster, us, eu) in new[] { ("messages", _messagesUs, _messagesEu), ("identity", _identityUs, _identityEu) })
            {
                // "eu" sorts before "us", so the order under test isn't just the configuration's order.
                builder.UseSetting($"ReverseProxy:Clusters:{cluster}:Destinations:eu:Address", eu.Address);
                builder.UseSetting($"ReverseProxy:Clusters:{cluster}:Destinations:us:Address", us.Address);
                // Faster than in the app, so the tests don't wait long.
                builder.UseSetting($"ReverseProxy:Clusters:{cluster}:HealthCheck:Active:Interval", "00:00:00.100");
            }
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        foreach (var api in All)
            await api.DisposeAsync();
    }

    [Fact]
    public async Task AllRegionsUp_UsesTheFirstInTheOrder()
    {
        (await RegionAsync(Messages)).Should().Be("us");
        (await RegionAsync(Identity)).Should().Be("us");
    }

    [Fact]
    public async Task FirstRegionStops_UsesTheNext_AndReturnsWhenItIsBack()
    {
        await _messagesUs.StopAsync();
        await WaitForRegionAsync(Messages, "eu");

        await _messagesUs.StartAsync();
        await WaitForRegionAsync(Messages, "us");
    }

    [Fact]
    public async Task FirstRegionAnswersUnhealthy_UsesTheNext()
    {
        _messagesUs.HealthStatus = StatusCodes.Status503ServiceUnavailable;

        await WaitForRegionAsync(Messages, "eu");
    }

    [Fact]
    public async Task OneServiceStops_TheOtherStaysInItsRegion()
    {
        await _messagesUs.StopAsync();
        await WaitForRegionAsync(Messages, "eu");

        (await RegionAsync(Identity)).Should().Be("us");
    }

    [Fact]
    public async Task EveryRegionAnswersUnhealthy_RequestsAreStillTriedInOrder()
    {
        _messagesUs.HealthStatus = StatusCodes.Status503ServiceUnavailable;
        await WaitForRegionAsync(Messages, "eu");

        _messagesEu.HealthStatus = StatusCodes.Status503ServiceUnavailable;

        await WaitForRegionAsync(Messages, "us");
    }

    [Fact]
    public async Task EveryRegionStops_FailsInsteadOfHanging()
    {
        await _messagesUs.StopAsync();
        await _messagesEu.StopAsync();

        using var response = await _client.GetAsync(Messages);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadGateway, HttpStatusCode.GatewayTimeout);
    }

    private Task WaitForRegionAsync(string path, string region) =>
        WaitAsync(async () => await RegionAsync(path) == region);

    // The region that answered, or null if the request failed.
    private async Task<string?> RegionAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        return response.IsSuccessStatusCode ? response.Headers.GetValues("X-Pingo-Region").Single() : null;
    }

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the edge should have noticed by now");
            await Task.Delay(50);
        }
    }
}
