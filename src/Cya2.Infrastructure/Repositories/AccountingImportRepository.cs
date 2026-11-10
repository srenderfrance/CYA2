using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Cya2.Infrastructure.Services;

namespace Cya2.Infrastructure.Repositories;

public sealed class AccountingImportRepository : IAccountingImportRepository
{
    private readonly IConfiguration _config;
    private readonly ILogger<AccountingImportRepository> _logger;
    private readonly IImportProgressService _progress;
    private readonly IDatabaseGuard _dbGuard;

    public AccountingImportRepository(
        IConfiguration config,
        ILogger<AccountingImportRepository> logger,
        IImportProgressService progress,
        IDatabaseGuard dbGuard)
    {
        _config = config;
        _logger = logger;
        _progress = progress;
        _dbGuard = dbGuard;
    }

    private string ConnStr => _config.GetConnectionString("default") ?? string.Empty;

    private async Task BackupAllAndDeleteFromDateAsync(MySqlConnection conn, MySqlTransaction tx, DateTime fromDate, string backupId, CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();
        try
        {
            var countCmd = conn.CreateCommand();
            countCmd.Transaction = (MySqlTransaction)tx;
            countCmd.CommandTimeout = 30;
            countCmd.CommandText = "SELECT COUNT(*) FROM AccountingData";
            var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

            var snapshot = conn.CreateCommand();
            snapshot.Transaction = (MySqlTransaction)tx;
            snapshot.CommandText = @"
                INSERT INTO AccountingBackupSnapshots
                    (BackupId, BackupAt, SourceRangeStart, RecordCount, Pinned)
                SELECT @bid,
                       GREATEST(UTC_TIMESTAMP(6), COALESCE(DATE_ADD(MAX(BackupAt), INTERVAL 1 MICROSECOND), UTC_TIMESTAMP(6))),
                       @from, @count, 0
                FROM AccountingBackupSnapshots";
            snapshot.Parameters.Add(new MySqlParameter("@bid", backupId));
            snapshot.Parameters.Add(new MySqlParameter("@from", fromDate));
            snapshot.Parameters.Add(new MySqlParameter("@count", rowCount));
            await snapshot.ExecuteNonQueryAsync(ct);

            if (rowCount > 0)
            {

                var insert = conn.CreateCommand();
                insert.Transaction = (MySqlTransaction)tx;
                insert.CommandTimeout = 300;
                insert.CommandText = @"INSERT INTO AccountingDataBackup
                    (Id,AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated,BackupId,BackupAt,Pinned,SourceRangeStart)
                    SELECT Id,AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated,@bid,UTC_TIMESTAMP(),0,@from
                    FROM AccountingData";
                insert.Parameters.Add(new MySqlParameter("@bid", backupId));
                insert.Parameters.Add(new MySqlParameter("@from", fromDate));
                await insert.ExecuteNonQueryAsync(ct);

                var del = conn.CreateCommand();
                del.Transaction = (MySqlTransaction)tx;
                del.CommandTimeout = 300;
                del.CommandText = "DELETE FROM AccountingData WHERE Date >= @from";
                del.Parameters.Add(new MySqlParameter("@from", fromDate));
                await del.ExecuteNonQueryAsync(ct);

            }
            await CleanupBackupsAsync(conn, tx, backupId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError("Accounting backup/delete failed. ExceptionType={ExceptionType}", ex.GetType().Name);
            throw;
        }
    }

    public async Task<ImportBatchResult> ReplaceAsync(IReadOnlyList<AccountingImportRowDto> rows, CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();
        ct.ThrowIfCancellationRequested();
        if (rows.Count == 0) return new ImportBatchResult();
        await using var connection = new MySqlConnection(ConnStr);
        await connection.OpenAsync(ct);
        await FinancialWriteCoordinator.AcquireDatabaseLockAsync(connection, ct);
        try
        {
            await using (var stage = new MySqlCommand("DROP TEMPORARY TABLE IF EXISTS TempImportAccounting; CREATE TEMPORARY TABLE TempImportAccounting LIKE AccountingData", connection))
                await stage.ExecuteNonQueryAsync(ct);
            foreach (var batch in rows.Chunk(500))
            {
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 60;
                var values = new List<string>();
                for (var i = 0; i < batch.Length; i++)
                {
                    values.Add($"(@c{i},@d{i},@n{i},@a{i},@an{i},@ac{i},@t{i},@dc{i})");
                    command.Parameters.AddWithValue($"@c{i}", batch[i].AccountingClass);
                    command.Parameters.AddWithValue($"@d{i}", batch[i].Date);
                    command.Parameters.AddWithValue($"@n{i}", batch[i].Num);
                    command.Parameters.AddWithValue($"@a{i}", batch[i].Amount);
                    command.Parameters.AddWithValue($"@an{i}", batch[i].AccountNumber);
                    command.Parameters.AddWithValue($"@ac{i}", batch[i].Account);
                    command.Parameters.AddWithValue($"@t{i}", batch[i].Type);
                    command.Parameters.AddWithValue($"@dc{i}", batch[i].DateCreated);
                }
                command.CommandText = $"INSERT INTO TempImportAccounting (AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated) VALUES {string.Join(',', values)}";
                await command.ExecuteNonQueryAsync(ct);
            }
            await using var transaction = await connection.BeginTransactionAsync(ct);
            var backupId = Guid.NewGuid().ToString();
            try
            {
                await BackupAllAndDeleteFromDateAsync(connection, transaction, rows.Min(row => row.Date), backupId, ct);
                await using var publish = new MySqlCommand(@"INSERT INTO AccountingData (AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated)
SELECT AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated FROM TempImportAccounting", connection, transaction) { CommandTimeout = 300 };
                var inserted = await publish.ExecuteNonQueryAsync(ct);
                if (inserted != rows.Count) throw new InvalidOperationException("Staged accounting count did not match publication count.");
                await ImportTransaction.CommitAsync(transaction, ConnStr, "AccountingBackupSnapshots", backupId, ct);
                return new ImportBatchResult { Inserted = inserted };
            }
            catch
            {
                await ImportTransaction.RollbackAsync(transaction);
                throw;
            }
        }
        finally { await FinancialWriteCoordinator.ReleaseDatabaseLockAsync(connection); }
    }

    private static async Task CleanupBackupsAsync(MySqlConnection conn, MySqlTransaction tx, string backupId, CancellationToken ct)
    {
        foreach (var table in new[] { "AccountingDataBackup", "AccountingBackupSnapshots" })
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 60;
            cmd.CommandText = table == "AccountingBackupSnapshots"
                ? "DELETE FROM AccountingBackupSnapshots WHERE BackupId <> @BackupId AND Pinned = 0"
                : "DELETE FROM AccountingDataBackup WHERE BackupId <> @BackupId AND BackupId NOT IN (SELECT BackupId FROM AccountingBackupSnapshots WHERE Pinned = 1)";
            cmd.Parameters.AddWithValue("@BackupId", backupId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
