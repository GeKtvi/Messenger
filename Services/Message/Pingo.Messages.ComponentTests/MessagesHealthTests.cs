using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Pingo.Messages.ComponentTests;

// Load balancers probe /health to decide whether to send traffic to this instance.
public class MessagesHealthTests(MessagesApiFactory factory) : IClassFixture<MessagesApiFactory>
{
    [Fact]
    public async Task Health_WithoutAToken_ReturnsHealthy()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Health_IgnoresFailingDependencies()
    {
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("broken dependency", () => HealthCheckResult.Unhealthy())));

        var response = await app.CreateClient().GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_IsNotTraced_ButOtherRequestsAre()
    {
        using var client = factory.CreateClient();

        await client.GetAsync("/health");
        await client.GetAsync("/api/messages");

        // A span ends just after its response is sent, hence the wait. The health request finished
        // first, so its span would be recorded by then too.
        for (var i = 0; i < 100 && !TracedPaths().Contains("/api/messages"); i++)
            await Task.Delay(50);
        TracedPaths().Should().Contain("/api/messages").And.NotContain("/health");
    }

    private List<string?> TracedPaths() =>
        factory.Spans.Select(s => s.GetTagItem("url.path") as string).ToList();
}
