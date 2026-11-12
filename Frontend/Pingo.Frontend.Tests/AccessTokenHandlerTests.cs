using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Tests;

// The Message API client's pipeline: AccessTokenHandler in front of a fake Message API, with a real
// AuthSession talking to a fake Identity.
public class AccessTokenHandlerTests
{
    private readonly FakeIdentityApi _identity = new();
    private readonly FakeMessageApi _messages = new();
    private readonly IAuthSession _session;
    private readonly HttpClient _client;

    public AccessTokenHandlerTests()
    {
        _identity.Issued += token =>
        {
            lock (_messages.ValidTokens)
                _messages.ValidTokens.Add(token);
        };
        _session = new AuthSession(new IdentityApiClient(
            new HttpClient(_identity) { BaseAddress = new Uri("http://identity/") }));
        _client = new HttpClient(new AccessTokenHandler(_session) { InnerHandler = _messages })
        {
            BaseAddress = new Uri("http://messages/"),
        };
    }

    [Fact]
    public async Task ValidToken_SendsItOnce_WithoutRefreshing()
    {
        await SignInAsync();

        var response = await _client.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _messages.Received.Should().ContainSingle().Which.BearerToken.Should().Be("token-1");
        _identity.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExpiredToken_RefreshesAndRetriesWithTheNewToken()
    {
        await SignInAsync();
        ExpireAllTokens();

        var response = await _client.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _identity.RefreshCalls.Should().Be(1);
        _messages.Received.Select(r => r.BearerToken).Should().Equal("token-1", "token-2");
        _session.AccessToken.Should().Be("token-2");
    }

    [Fact]
    public async Task ExpiredToken_RetryResendsTheSameBody()
    {
        await SignInAsync();
        ExpireAllTokens();

        // JsonContent, like PutAsJsonAsync in MessageApiClient: it serializes again on every send.
        await _client.PutAsync("api/messages/42", JsonContent.Create(new { text = "hello" }));

        _messages.Received.Should().HaveCount(2);
        _messages.Received.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Put);
            r.Path.Should().Be("/api/messages/42");
            r.Body.Should().Be("{\"text\":\"hello\"}");
        });
    }

    [Fact]
    public async Task ExpiredToken_WithAStreamBody_RefreshesButLeavesTheResendToTheCaller()
    {
        // The stream is consumed by the first send, and buffering it would hold a whole upload in memory.
        await SignInAsync();
        ExpireAllTokens();
        var body = new StreamContent(new ForwardOnlyStream(Encoding.UTF8.GetBytes("{\"text\":\"hello\"}")));

        var response = await _client.PutAsync("api/messages/42", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _messages.Received.Should().ContainSingle();
        _session.AccessToken.Should().Be("token-2", "the caller's next attempt goes out with the new token");
    }

    [Fact]
    public async Task ManyRequestsGet401AtOnce_ShareOneRefresh()
    {
        // Refresh tokens work once: two refreshes in parallel would spend the cookie twice.
        await SignInAsync();
        ExpireAllTokens();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => _client.GetAsync("api/messages")));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        _identity.RefreshCalls.Should().Be(1);
        _messages.Received.Where(r => r.BearerToken == "token-2").Should().HaveCount(10);
    }

    [Fact]
    public async Task SessionEnded_Returns401AndSignsOut()
    {
        await SignInAsync();
        ExpireAllTokens();
        _identity.SessionValid = false;

        var response = await _client.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _session.IsSignedIn.Should().BeFalse("the login form comes back");
        _messages.Received.Should().ContainSingle("there's no token to retry with");
    }

    [Fact]
    public async Task NewTokenAlsoRejected_DoesNotRetryAgain()
    {
        await SignInAsync();
        _identity.Issued += _ => ExpireAllTokens();
        ExpireAllTokens();

        var response = await _client.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _messages.Received.Should().HaveCount(2);
        _identity.RefreshCalls.Should().Be(1);
    }

    [Fact]
    public async Task NotSignedIn_SendsNoTokenAndDoesNotRefresh()
    {
        var response = await _client.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _messages.Received.Should().ContainSingle().Which.BearerToken.Should().BeNull();
        _identity.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task TokenRefreshedByAnotherRequestMeanwhile_RetriesWithoutRefreshingAgain()
    {
        // This request was sent with token-1; by the time its 401 arrives, another request has
        // already refreshed to token-2.
        await SignInAsync();
        ExpireAllTokens();
        var slowMessages = new DelayedHandler(_messages, () => _session.RefreshAsync());
        using var client = new HttpClient(new AccessTokenHandler(_session) { InnerHandler = slowMessages })
        {
            BaseAddress = new Uri("http://messages/"),
        };

        var response = await client.GetAsync("api/messages");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _identity.RefreshCalls.Should().Be(1);
        _messages.Received.Select(r => r.BearerToken).Should().Equal("token-1", "token-2");
    }

    private async Task SignInAsync() => (await _session.LoginAsync("ann@example.com", "password")).Should().BeNull();

    // Like the access tokens' 15 minutes running out: the Message API stops accepting them.
    private void ExpireAllTokens()
    {
        lock (_messages.ValidTokens)
            _messages.ValidTokens.Clear();
    }

    // A body that can be read only once, like a network or file stream.
    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    // Runs an action after the first request reaches the server and before its response returns.
    private sealed class DelayedHandler(HttpMessageHandler inner, Func<Task> beforeFirstResponse) : DelegatingHandler(inner)
    {
        private int _calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (Interlocked.Increment(ref _calls) == 1)
                await beforeFirstResponse();
            return response;
        }
    }
}
