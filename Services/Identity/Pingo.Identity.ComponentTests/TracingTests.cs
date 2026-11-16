using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Pingo.Identity.Api.Contracts;

namespace Pingo.Identity.ComponentTests;

// A login is one trace: the HTTP request, its database calls and the UserLoggedIn publish, which
// carries the trace on to the Message service.
[Collection(IdentityCollection.Name)]
[Trait("Category", "Docker")]
public class TracingTests(IdentityApiFactory factory) : IdentityTestBase(factory)
{
    [Fact]
    public async Task Login_IsOneTraceWithHttpDatabaseAndPublishSpans()
    {
        await Client.RegisterAsync("ivan@example.com");
        // The caller's trace id, as a browser or gateway would send it: the login must continue it.
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = "ivan@example.com", Password = IdentityHttpClientExtensions.Password }),
        };
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        var trace = await Factory.Spans.WaitForTraceAsync(traceId, s => s.Kind == ActivityKind.Server);
        var http = trace.Should().ContainSingle(s => s.Kind == ActivityKind.Server).Subject;
        http.DisplayName.Should().Be("POST api/auth/login");

        var database = trace.Where(s => s.Source.Name == "Npgsql").ToList();
        database.Should().NotBeEmpty().And.OnlyContain(s => IsInside(s, http, trace));

        var publish = trace.Should().ContainSingle(s => s.Source.Name == "MassTransit" && s.Kind == ActivityKind.Producer).Subject;
        IsInside(publish, http, trace).Should().BeTrue("the publish happens during the login request");
    }

    [Fact]
    public async Task FailedLogin_HasNoPublishSpan()
    {
        var traceId = ActivityTraceId.CreateRandom();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = "nobody@example.com", Password = "whatever-pass" }),
        };
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        (await Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var trace = await Factory.Spans.WaitForTraceAsync(traceId, s => s.Kind == ActivityKind.Server);
        trace.Should().Contain(s => s.Kind == ActivityKind.Server);
        trace.Should().NotContain(s => s.Source.Name == "MassTransit");
    }

    // Walks up the parent chain within the trace.
    private static bool IsInside(Activity span, Activity ancestor, IReadOnlyList<Activity> trace)
    {
        var bySpanId = trace.ToDictionary(s => s.SpanId);
        for (var current = span; bySpanId.TryGetValue(current.ParentSpanId, out var parent); current = parent)
        {
            if (parent.SpanId == ancestor.SpanId)
                return true;
        }

        return false;
    }
}
