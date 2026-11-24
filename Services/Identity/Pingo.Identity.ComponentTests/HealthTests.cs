using System.Net;
using FluentAssertions;

namespace Pingo.Identity.ComponentTests;

// Load balancers probe /health to decide whether to send traffic to this instance.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class HealthTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
