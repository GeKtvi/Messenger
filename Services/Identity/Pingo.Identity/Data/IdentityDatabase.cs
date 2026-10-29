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

    public void MarkReady() => _ready.TrySetResult();

    public void MarkFailed(Exception error) => _ready.TrySetException(error);

    public ValueTask DisposeAsync() => DataSource.DisposeAsync();
}
