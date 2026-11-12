using System.Net;
using System.Net.Http.Json;
using Pingo.Frontend.Services.Auth;

namespace Pingo.Frontend.Tests;

// A request as the fake server saw it.
public record ReceivedRequest(HttpMethod Method, string Path, string? BearerToken, string? Body);

// Stands in for the Message API: accepts only the tokens in ValidTokens, otherwise answers 401.
public class FakeMessageApi : HttpMessageHandler
{
    private readonly List<ReceivedRequest> _received = [];

    public HashSet<string> ValidTokens { get; } = [];

    public IReadOnlyList<ReceivedRequest> Received
    {
        get { lock (_received) return [.. _received]; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = request.Headers.Authorization?.Parameter;
        var body = request.Content is null ? null : await ReadLikeANetworkAsync(request.Content, ct);
        lock (_received)
            _received.Add(new ReceivedRequest(request.Method, request.RequestUri!.AbsolutePath, token, body));

        return token is not null && ValidTokens.Contains(token)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body ?? "ok") }
            : new HttpResponseMessage(HttpStatusCode.Unauthorized);
    }

    // Copies the body out the way a real transport does. ReadAsStringAsync would buffer the content
    // as a side effect and hide a body that can't be sent twice.
    private static async Task<string> ReadLikeANetworkAsync(HttpContent content, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        return System.Text.Encoding.UTF8.GetString(copy.ToArray());
    }
}

// Stands in for Identity: login gives "token-1", each successful refresh the next number.
public class FakeIdentityApi : HttpMessageHandler
{
    private int _issued;
    private int _refreshCalls;

    // When false, /refresh answers 401 as if the session had ended.
    public bool SessionValid { get; set; } = true;

    // Holds each refresh this long, so parallel requests overlap with it.
    public TimeSpan RefreshDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    public int RefreshCalls => _refreshCalls;

    // Raised with every token this fake issues, so the Message API fake can accept it.
    public event Action<string>? Issued;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/api/auth/login":
                return Tokens();
            case "/api/auth/refresh":
                Interlocked.Increment(ref _refreshCalls);
                await Task.Delay(RefreshDelay, ct);
                return SessionValid ? Tokens() : new HttpResponseMessage(HttpStatusCode.Unauthorized);
            default:
                return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private HttpResponseMessage Tokens()
    {
        var token = $"token-{Interlocked.Increment(ref _issued)}";
        Issued?.Invoke(token);
        var now = DateTimeOffset.UtcNow;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TokenResponse(token, now.AddMinutes(15), now.AddDays(30))),
        };
    }
}
