using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pingo.Frontend.Services;
using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Tests;

// The Message API client exactly as the app registers it (AddPingoApiClients): token handler outside,
// retries inside. Only the network is replaced, and the delays are shortened.
public class MessageApiResilienceTests : IDisposable
{
    private readonly ScriptedMessageApi _messages = new();
    private readonly FakeIdentityApi _identity = new();
    private readonly ServiceProvider _services;

    public MessageApiResilienceTests()
    {
        var services = new ServiceCollection();
        services.AddPingoApiClients(new Uri("http://messages/"), new Uri("http://identity/"));
        services.AddHttpClient<MessageApiClient>().ConfigurePrimaryHttpMessageHandler(() => _messages);
        services.AddHttpClient<IdentityApiClient>().ConfigurePrimaryHttpMessageHandler(() => _identity);
        services.Configure<MessageApiRetryOptions>(options =>
        {
            options.Delay = TimeSpan.FromMilliseconds(10);
            options.AttemptTimeout = TimeSpan.FromMilliseconds(300);
        });
        _services = services.BuildServiceProvider();
    }

    private MessageApiClient Api => _services.GetRequiredService<MessageApiClient>();

    // The same pipeline without MessageApiClient's parsing, to look at raw responses.
    private HttpClient RawClient => _services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MessageApiClient));

    [Fact]
    public async Task ServerErrors_AreRetriedUntilOneSucceeds()
    {
        await SignInAsync();
        _messages.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.InternalServerError);

        var messages = await Api.GetAllAsync();

        messages.Should().BeEmpty();
        _messages.Received.Should().HaveCount(3);
    }

    [Fact]
    public async Task ServerKeepsFailing_GivesUpAfterThreeRetries()
    {
        await SignInAsync();
        for (var i = 0; i < 10; i++)
            _messages.Respond(HttpStatusCode.ServiceUnavailable);

        var response = await RawClient.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        _messages.Received.Should().HaveCount(4, "the first attempt and three retries");
    }

    [Fact]
    public async Task ServerUnreachable_IsRetried()
    {
        await SignInAsync();
        _messages.FailConnection().FailConnection();

        var messages = await Api.GetAllAsync();

        messages.Should().BeEmpty();
        _messages.Received.Should().HaveCount(3);
    }

    [Fact]
    public async Task AttemptTakingTooLong_IsCancelledAndRetried()
    {
        await SignInAsync();
        _messages.Hang();

        var messages = await Api.GetAllAsync();

        messages.Should().BeEmpty();
        _messages.Received.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task ClientErrors_AreNotRetried(HttpStatusCode status)
    {
        // Sending the same wrong request again can't fix it.
        await SignInAsync();
        _messages.Respond(status);

        var response = await RawClient.GetAsync("api/messages");

        response.StatusCode.Should().Be(status);
        _messages.Received.Should().ContainSingle();
    }

    [Fact]
    public async Task Send_IsRetriedWithTheSameIdAndBody()
    {
        // Safe because PUT /api/messages/{id} is idempotent: a repeat never creates a duplicate.
        await SignInAsync();
        _messages.Respond(HttpStatusCode.BadGateway);
        var id = Guid.NewGuid();

        await Api.SendAsync(id, "hello");

        _messages.Received.Should().HaveCount(2).And.AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Put);
            r.Path.Should().Be($"/api/messages/{id}");
            r.Body.Should().Be("{\"text\":\"hello\"}");
        });
    }

    [Fact]
    public async Task ExpiredTokenIsNotRetriedByPolly_TheTokenHandlerRefreshesInstead()
    {
        await SignInAsync();
        _messages.Respond(HttpStatusCode.Unauthorized);

        var messages = await Api.GetAllAsync();

        messages.Should().BeEmpty();
        _identity.RefreshCalls.Should().Be(1);
        _messages.Received.Select(r => r.BearerToken).Should().Equal("token-1", "token-2");
    }

    [Fact]
    public async Task ServerErrorThenExpiredToken_BothAreHandled()
    {
        // 503 is retried by the resilience handler; the 401 that follows goes up to the token handler,
        // which refreshes and sends again through the retries.
        await SignInAsync();
        _messages.Respond(HttpStatusCode.ServiceUnavailable).Respond(HttpStatusCode.Unauthorized);

        var messages = await Api.GetAllAsync();

        messages.Should().BeEmpty();
        _identity.RefreshCalls.Should().Be(1);
        _messages.Received.Select(r => r.BearerToken).Should().Equal("token-1", "token-1", "token-2");
    }

    private async Task SignInAsync() =>
        (await _services.GetRequiredService<IAuthSession>().LoginAsync("ann@example.com", "password")).Should().BeNull();

    public void Dispose() => _services.Dispose();
}
