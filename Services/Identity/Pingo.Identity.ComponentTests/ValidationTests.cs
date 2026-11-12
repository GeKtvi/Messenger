using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;

namespace Pingo.Identity.ComponentTests;

// Request bodies [ApiController] rejects with 400 before an action runs, at the exact limits.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class ValidationTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private const string Domain = "@example.com";

    [Fact]
    public async Task Register_EmailAtTheLengthLimit_Succeeds()
    {
        var response = await Client.RegisterAsync(new string('a', 254 - Domain.Length) + Domain);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_EmailOverTheLengthLimit_Returns400()
    {
        var response = await Client.RegisterAsync(new string('a', 255 - Domain.Length) + Domain);

        (await response.ReadValidationErrorsAsync())["Email"].Should().Equal("Email must be at most 254 characters.");
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        var response = await Client.RegisterAsync("not-an-email");

        (await response.ReadValidationErrorsAsync())["Email"].Should().Equal("Email is not a valid email address.");
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public async Task Register_PasswordWithinTheLengthLimits_Succeeds(int length)
    {
        var response = await Client.RegisterAsync("ann@example.com", new string('p', length));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(7, "Password must be at least 8 characters.")]
    [InlineData(129, "Password must be at most 128 characters.")]
    public async Task Register_PasswordOutsideTheLengthLimits_Returns400(int length, string message)
    {
        var response = await Client.RegisterAsync("ann@example.com", new string('p', length));

        (await response.ReadValidationErrorsAsync())["Password"].Should().Equal(message);
    }

    [Fact]
    public async Task Register_EmptyBody_Returns400ForBothFields()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new { });

        // The format and length rules reject an empty string too, so each field gets a second message.
        var errors = await response.ReadValidationErrorsAsync();
        errors["Email"].Should().Contain("Email is required.");
        errors["Password"].Should().Contain("Password is required.");
    }

    [Fact]
    public async Task Register_NullFields_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new { email = (string?)null, password = (string?)null });

        (await response.ReadValidationErrorsAsync()).Keys.Should().Contain(["Email", "Password"]);
    }

    [Fact]
    public async Task Register_MalformedJson_Returns400()
    {
        var response = await Client.PostAsync(
            "/api/auth/register", new StringContent("{\"email\": ", Encoding.UTF8, "application/json"));

        (await response.ReadValidationErrorsAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Login_WhitespaceOnlyFields_Returns400NotAServerError()
    {
        var response = await Client.LoginAsync("   ", "   ");

        var errors = await response.ReadValidationErrorsAsync();
        errors["Email"].Should().Equal("Email is required.");
        errors["Password"].Should().Equal("Password is required.");
    }
}
