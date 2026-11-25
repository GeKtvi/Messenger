using Npgsql;

namespace Pingo.Identity.Data;

// Hands out connections to the Identity database. The migrations run in the background while the
// app is already accepting requests, so connections are held back until the schema is ready.
internal sealed class IdentityDatabase(string connectionString) : IAsyncDisposable
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ConnectionString { get; } = connectionString;

    public NpgsqlDataSource DataSource { get; } = NpgsqlDataSource.Create(connectionString);

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        await _ready.Task.WaitAsync(ct);
        return await DataSource.OpenConnectionAsync(ct);
    }

    // Runs work on a connection of its own. If that connection turns out to be dead, as idle pooled
    // connections are after PostgreSQL restarts, the work runs once more on a new connection. Npgsql
    // empties the pool when it finds a dead connection, so one retry is enough. A dead connection fails on
    // its first command, before anything is stored.
    public async Task<T> RunAsync<T>(Func<NpgsqlConnection, Task<T>> work, CancellationToken ct = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(ct);
            return await work(connection);
        }
        catch (NpgsqlException ex) when (ex.IsTransient && !ct.IsCancellationRequested)
        {
            // With several hosts (D-070), the failure marked the server as down for a few seconds, so the
            // retry would find no server at all. It's most likely back, so check again.
            if (DataSource is NpgsqlMultiHostDataSource multiHost)
                multiHost.ClearDatabaseStates();
            await using var connection = await OpenConnectionAsync(ct);
            return await work(connection);
        }
    }

    public Task RunAsync(Func<NpgsqlConnection, Task> work, CancellationToken ct = default) =>
        RunAsync(async connection =>
        {
            await work(connection);
            return true;
        }, ct);

    public void MarkReady() => _ready.TrySetResult();

    public void MarkFailed(Exception error) => _ready.TrySetException(error);

    public ValueTask DisposeAsync() => DataSource.DisposeAsync();
}
