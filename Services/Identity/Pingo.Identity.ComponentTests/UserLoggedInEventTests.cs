using System.Net;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Pingo.Contracts;

namespace Pingo.Identity.ComponentTests;

// Identity tells other services about each successful login with a UserLoggedIn event.
// The harness keeps events from earlier tests, so each test uses its own email.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class UserLoggedInEventTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    private ITestHarness Harness => Factory.Services.GetTestHarness();

    [Fact]
    public async Task Login_PublishesUserLoggedInWithTheNormalizedEmail()
    {
        await Client.RegisterAsync("erin@example.com");

        (await Client.LoginAsync("  Erin@Example.COM ")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Harness.Published.Any<UserLoggedIn>(p => p.Context.Message.Email == "erin@example.com"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task EachLogin_PublishesItsOwnEvent()
    {
        await Client.RegisterAsync("frank@example.com");

        await Client.LoginAsync("frank@example.com");
        await Client.LoginAsync("frank@example.com");

        var published = Harness.Published.SelectAsync<UserLoggedIn>(p => p.Context.Message.Email == "frank@example.com");
        (await AsyncElementListExtensions.Take(published, 2).CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task FailedLogin_PublishesNothing()
    {
        await Client.RegisterAsync("grace@example.com");

        (await Client.LoginAsync("grace@example.com", "wrong password!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client.LoginAsync("nobody-like-grace@example.com")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Harness.Published.Any<UserLoggedIn>(p => p.Context.Message.Email.Contains("grace")))
            .Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAndRefresh_PublishNothing()
    {
        // Only a login with a password counts as logging in; a refresh just continues a session.
        var login = await Client.RegisterAndLoginAsync("heidi@example.com");

        (await Client.RefreshAsync(login.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Harness.Published.SelectAsync<UserLoggedIn>(p => p.Context.Message.Email == "heidi@example.com")
            .CountAsync()).Should().Be(1);
    }
}
