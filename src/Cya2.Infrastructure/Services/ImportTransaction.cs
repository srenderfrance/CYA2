using Cya2.Core.Interfaces;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Services;

internal static class ImportTransaction
{
    public static async Task CommitAsync(MySqlTransaction transaction, string connectionString, string snapshotTable, string backupId, CancellationToken cancellationToken, bool requirePinned = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Once publication starts, a disconnected caller cannot cancel the commit acknowledgement.
            await transaction.CommitAsync(CancellationToken.None);
        }
        catch
        {
            try
            {
                await using var verification = new MySqlConnection(connectionString);
                await verification.OpenAsync(CancellationToken.None);
                await using var command = new MySqlCommand($"SELECT COUNT(*) FROM {snapshotTable} WHERE BackupId=@id{(requirePinned ? " AND Pinned = 1" : string.Empty)}", verification) { CommandTimeout = 10 };
                command.Parameters.AddWithValue("@id", backupId);
                if (Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None)) == 1) return;
            }
            catch { }
            throw new ImportOutcomeUnknownException();
        }
    }

    public static async Task RollbackAsync(MySqlTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch { }
    }
}
