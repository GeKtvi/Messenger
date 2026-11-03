using System.Net;
using FluentAssertions;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class RegisterTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Email = "ann@example.com";

    [Fact]
    public async Task Register_NewEmail_Returns201WithUserId()
    {
        var response = await Client.RegisterAsync(Email);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var registered = await response.ReadAsync<RegisterResponse>();
        registered.UserId.Should().NotBeEmpty();
        registered.RegisteredAt.Should().Be(Factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task Register_TakenEmail_Returns409EmailTaken()
    {
        await Client.RegisterAsync(Email);

        var response = await Client.RegisterAsync(Email, "another password");

        await response.ShouldBeProblemAsync(
            HttpStatusCode.Conflict, "email-taken", "A user with this email already exists.");
    }

    [Fact]
    public async Task Register_TakenEmailInOtherCase_Returns409EmailTaken()
    {
        await Client.RegisterAsync(Email);

        var response = await Client.RegisterAsync("  Ann@Example.COM ");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
