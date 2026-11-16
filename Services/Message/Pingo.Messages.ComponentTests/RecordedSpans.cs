using System.Collections;
using System.Diagnostics;

namespace Pingo.Messages.ComponentTests;

// Finished spans, collected by OpenTelemetry's in-memory exporter. Spans end on several threads
// (the request, MassTransit's consumers), so access is locked.
public sealed class RecordedSpans : ICollection<Activity>
{
    private readonly List<Activity> _spans = [];

    public int Count
    {
        get { lock (_spans) return _spans.Count; }
    }

    public bool IsReadOnly => false;

    // The spans of one trace, once a span matching the condition has been recorded (or after 10 s).
    public async Task<IReadOnlyList<Activity>> WaitForTraceAsync(ActivityTraceId traceId, Func<Activity, bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var trace = Snapshot().Where(s => s.TraceId == traceId).ToList();
            if (trace.Any(until) || DateTime.UtcNow > deadline)
                return trace;
            await Task.Delay(50);
        }
    }

    public void Add(Activity item)
    {
        lock (_spans) _spans.Add(item);
    }

    public void Clear()
    {
        lock (_spans) _spans.Clear();
    }

    public bool Contains(Activity item)
    {
        lock (_spans) return _spans.Contains(item);
    }

    public void CopyTo(Activity[] array, int arrayIndex)
    {
        lock (_spans) _spans.CopyTo(array, arrayIndex);
    }

    public bool Remove(Activity item)
    {
        lock (_spans) return _spans.Remove(item);
    }

    public IEnumerator<Activity> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<Activity> Snapshot()
    {
        lock (_spans) return [.. _spans];
    }
}
