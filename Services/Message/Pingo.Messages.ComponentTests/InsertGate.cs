using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pingo.Messages.ComponentTests;

// Makes a race happen on purpose. Concurrent requests rarely collide by chance, because the gap
// between "id not found" and "insert" is tiny. The gate holds each request right before its insert
// until all expected requests have arrived, then releases them together, so they're guaranteed to
// insert the same id at the same moment.
public class InsertGate(int participants)
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrived;

    public int Arrived => Volatile.Read(ref _arrived);

    public Task ArriveAsync(CancellationToken ct)
    {
        if (Interlocked.Increment(ref _arrived) == participants)
            _allArrived.TrySetResult();

        // If too few requests arrive, fail loudly instead of hanging the test run.
        return _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
    }
}

// Calls the gate before any SaveChanges that inserts a row. Updates pass straight through.
public class InsertGateInterceptor(Func<InsertGate?> gate) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var current = gate();
        if (current is not null && eventData.Context!.ChangeTracker.Entries().Any(e => e.State == EntityState.Added))
            await current.ArriveAsync(cancellationToken);

        return result;
    }
}
