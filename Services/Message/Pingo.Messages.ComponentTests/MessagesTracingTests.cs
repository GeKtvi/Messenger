using System.Diagnostics;
using FluentAssertions;
using MassTransit.Testing;
using Pingo.Contracts;

namespace Pingo.Messages.ComponentTests;

// The greeting continues the trace of the login that caused it: MassTransit carries the trace in the
// message headers, and the consumer's database calls hang under its "process" span.
// Needs Docker (real PostgreSQL, for database spans). Skip with: dotnet test --filter "Category!=Docker"
[Trait("Category", "Docker")]
public class MessagesTracingTests(PostgresMessagesApiFactory factory)
    : IClassFixture<PostgresMessagesApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UserLoggedIn_ContinuesThePublishersTrace_WithDatabaseSpans()
    {
        var harness = factory.Services.GetTestHarness();

        // Stands in for Identity's login request: the event is published inside its span.
        ActivityTraceId traceId;
        using (var login = MessagesApiFactoryBase.TestActivities.StartActivity("login in Identity", ActivityKind.Server))
        {
            login.Should().NotBeNull("the app's tracer listens to the test source");
            traceId = login!.TraceId;
            await harness.Bus.Publish(new UserLoggedIn("ann@example.com"));
        }

        var trace = await factory.Spans.WaitForTraceAsync(traceId, s => IsConsumerProcess(s, "message-saved"));
        var process = trace.Should().ContainSingle(s => IsConsumerProcess(s, "user-logged-in")).Subject;
        var database = trace.Where(s => s.Source.Name == "Npgsql").ToList();
        database.Should().NotBeEmpty().And.OnlyContain(s => IsInside(s, process, trace));
        database.Select(Sql).Should().Contain(sql => sql.Contains("INSERT INTO"), "saving the greeting is part of the trace");
        trace.Should().ContainSingle(s => IsConsumerProcess(s, "message-saved"), "pushing the greeting to open chats is part of it too");
    }

    // A queue's name starts with its consumer's, e.g. "user-logged-in process".
    private static bool IsConsumerProcess(Activity span, string queue) =>
        span.Source.Name == "MassTransit" && span.Kind == ActivityKind.Consumer
        && span.DisplayName.StartsWith(queue) && span.DisplayName.EndsWith(" process");

    // The SQL of a database span (newer semantic conventions call the tag db.query.text).
    private static string Sql(Activity span) =>
        (span.GetTagItem("db.query.text") ?? span.GetTagItem("db.statement")) as string ?? string.Empty;

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
