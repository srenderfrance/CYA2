using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Services;

internal static class FinancialWriteCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    internal const string LockName = "cya2-financial-publication";

    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        return new Lease();
    }

    public static async Task<IAsyncDisposable> LockDatabaseAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await AcquireDatabaseLockAsync(connection, cancellationToken);
        return new DatabaseLease(connection);
    }

    private sealed class DatabaseLease(MySqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await ReleaseDatabaseLockAsync(connection);
    }

    public static async Task AcquireDatabaseLockAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new MySqlCommand("SELECT GET_LOCK(@name, 30)", connection) { CommandTimeout = 35 };
            command.Parameters.AddWithValue("@name", LockName);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new InvalidOperationException("Another financial write is in progress. Try again after it completes.");
        }
        catch
        {
            // Acquisition may succeed even when its acknowledgement is cancelled or lost.
            await ReleaseDatabaseLockAsync(connection);
            throw;
        }
    }

    public static async Task ReleaseDatabaseLockAsync(MySqlConnection connection)
    {
        try
        {
            await using var command = new MySqlCommand("SELECT RELEASE_LOCK(@name)", connection) { CommandTimeout = 5 };
            command.Parameters.AddWithValue("@name", LockName);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch
        {
            // Never return a possibly lock-owning session to the connection pool.
            MySqlConnection.ClearPool(connection);
        }
    }

    private sealed class Lease : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Gate.Release();
        }
    }
}
