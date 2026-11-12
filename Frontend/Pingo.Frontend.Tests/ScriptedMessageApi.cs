using System.Net;
using System.Net.Http.Json;
using Pingo.Frontend.Models;

namespace Pingo.Frontend.Tests;

// A Message API that answers from a script: each request takes the next step, and once the script
// runs out every request succeeds. Steps can return a status, hang, or fail like a dropped connection.
public class ScriptedMessageApi : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();
    private readonly List<ReceivedRequest> _received = [];

    public IReadOnlyList<ReceivedRequest> Received
    {
        get { lock (_received) return [.. _received]; }
    }

    public ScriptedMessageApi Respond(HttpStatusCode status)
    {
        _script.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        return this;
    }

    // Never answers: only the caller's timeout or cancellation ends it.
    public ScriptedMessageApi Hang()
    {
        _script.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    // Like a server that's down: the browser's fetch fails before any response.
    public ScriptedMessageApi FailConnection()
    {
        _script.Enqueue((_, _) => throw new HttpRequestException("Connection refused"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string? body = null;
        if (request.Content is not null)
        {
            using var copy = new MemoryStream();
            await request.Content.CopyToAsync(copy, ct);
            body = System.Text.Encoding.UTF8.GetString(copy.ToArray());
        }

        lock (_received)
        {
            _received.Add(new ReceivedRequest(
                request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter, body));
        }

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? step;
        lock (_script)
            _script.TryDequeue(out step);

        return step is not null ? await step(request, ct) : Success(request);
    }

    private static HttpResponseMessage Success(HttpRequestMessage request)
    {
        object content = request.Method == HttpMethod.Get
            ? new List<MessageDto>()
            : new MessageDto(Guid.Parse(request.RequestUri!.Segments[^1]), "hello", DateTimeOffset.UtcNow, null);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content) };
    }
}
