using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Tests;

// What a refresh does to the session when Identity fails, and when several tabs refresh at once.
public class AuthSessionTests
{
    private readonly CookieIdentityApi _identity = new();
    private readonly FakeRefreshLock _lock = new();

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Refresh_IdentityFails_StaysSignedIn(HttpStatusCode status)
    {
        var session = await SignInAsync();
        var token = session.AccessToken;
        _identity.RefreshFailure = () => new HttpResponseMessage(status);

        await session.RefreshAsync();

        session.AccessToken.Should().Be(token);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task Refresh_IdentityUnreachableOrTooSlow_StaysSignedInAndWorksOnceItIsBack(Type failure)
    {
        var session = await SignInAsync();
        var token = session.AccessToken;
        _identity.RefreshFailure = () => throw (Exception)Activator.CreateInstance(failure)!;

        await session.RefreshAsync();

        session.AccessToken.Should().Be(token);

        _identity.RefreshFailure = null;
        await session.RefreshAsync();

        session.AccessToken.Should().NotBeNull().And.NotBe(token);
    }

    [Fact]
    public async Task Refresh_SessionEnded_SignsOut()
    {
        var session = await SignInAsync();
        _identity.EndSession();

        await session.RefreshAsync();

        session.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_TwoTabsAtOnce_BothStaySignedIn()
    {
        var first = await SignInAsync();
        var second = NewTab();
        await second.RestoreAsync();

        await Task.WhenAll(first.RefreshAsync(), second.RefreshAsync());

        first.IsSignedIn.Should().BeTrue();
        second.IsSignedIn.Should().BeTrue();
        _identity.Refused.Should().Be(0, "each tab sends the cookie the one before it got");
    }

    private AuthSession NewTab() => new(
        new IdentityApiClient(new HttpClient(_identity, disposeHandler: false) { BaseAddress = new Uri("http://identity/") }),
        _lock);

    private async Task<AuthSession> SignInAsync()
    {
        var session = NewTab();
        (await session.LoginAsync("ann@example.com", "password")).Should().BeNull();
        return session;
    }

    // Identity as one browser sees it: a single cookie for all tabs, holding a refresh token that works
    // once. A request carries the cookie as it was when sent; the new one is stored when the answer arrives.
    private sealed class CookieIdentityApi : HttpMessageHandler
    {
        private int _issued;
        private int _valid;
        private int _cookie;
        private int _refused;

        public Func<HttpResponseMessage>? RefreshFailure { get; set; }

        public int Refused => _refused;

        public void EndSession() => _valid = -1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath != "/api/auth/refresh")
            {
                _valid = _cookie = Interlocked.Increment(ref _issued);
                return Tokens(_cookie);
            }

            if (RefreshFailure is { } failure)
                return failure();

            var sent = _cookie;
            // The trip to the server and back, during which another tab can send the same cookie.
            await Task.Delay(30, ct);
            var next = Interlocked.Increment(ref _issued);
            if (Interlocked.CompareExchange(ref _valid, next, sent) != sent)
            {
                Interlocked.Increment(ref _refused);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            _cookie = next;
            return Tokens(next);
        }

        private static HttpResponseMessage Tokens(int number)
        {
            var now = DateTimeOffset.UtcNow;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TokenResponse($"token-{number}", now.AddMinutes(15), now.AddDays(30))),
            };
        }
    }
}
