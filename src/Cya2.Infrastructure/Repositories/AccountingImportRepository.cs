using System.Globalization;
using System.Text;
using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

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

    public async Task BackupAllAndDeleteFromDateAsync(DateTime fromDate, string progressId, CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();
        try
        {
            _progress.UpdateStep(progressId, "Database Backup", "Connecting to database...");
            var csb = new MySqlConnectionStringBuilder(ConnStr)
            {
                ConnectionTimeout = 60,
                DefaultCommandTimeout = 300
            };

            await using var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            await EnsureBackupTableAsync(conn, (MySqlTransaction)tx, ct);

            string backupId = Guid.NewGuid().ToString();

            _progress.UpdateStep(progressId, "Database Backup", "Counting existing records...");
            var countCmd = conn.CreateCommand();
            countCmd.Transaction = (MySqlTransaction)tx;
            countCmd.CommandTimeout = 30;
            countCmd.CommandText = "SELECT COUNT(*) FROM AccountingData";
            var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

            if (rowCount > 0)
            {
                _progress.UpdateStep(progressId, "Database Backup", $"Backing up all {rowCount:N0} existing records...");

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

                _progress.UpdateStep(progressId, "Database Backup", $"Removing records from {fromDate:yyyy-MM-dd} forward...");
                var del = conn.CreateCommand();
                del.Transaction = (MySqlTransaction)tx;
                del.CommandTimeout = 300;
                del.CommandText = "DELETE FROM AccountingData WHERE Date >= @from";
                del.Parameters.Add(new MySqlParameter("@from", fromDate));
                await del.ExecuteNonQueryAsync(ct);

                _progress.UpdateStep(progressId, "Database Backup", "Cleaning up old backups...");
                await CleanupBackupsAsync(conn, (MySqlTransaction)tx, ct);
            }
            else
            {
                _progress.UpdateStep(progressId, "Database Backup", "No existing records found");
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Accounting backup/delete failed");
            _progress.UpdateStep(progressId, "Database Backup", $"Failed: {ex.Message}");
            throw;
        }
    }

    public async Task<ImportBatchResult> BulkInsertAsync(IReadOnlyList<AccountingImportRowDto> batch, CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();
        var result = new ImportBatchResult();
        if (batch == null || batch.Count == 0) return result;

        string tempFile = Path.Combine(Path.GetTempPath(), $"acct_import_{Guid.NewGuid():N}.csv");
        try
        {
            var sb = new StringBuilder();
            foreach (var r in batch)
            {
                static string Q(string? s) => s is null ? "" : '"' + s.Replace("\"", "\"\"") + '"';
                sb.Append(Q(r.AccountingClass)); sb.Append(',');
                sb.Append(Q(r.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))); sb.Append(',');
                sb.Append(Q(r.Num)); sb.Append(',');
                sb.Append(Q(r.Amount.ToString(CultureInfo.InvariantCulture))); sb.Append(',');
                sb.Append(Q(r.AccountNumber)); sb.Append(',');
                sb.Append(Q(r.Account)); sb.Append(',');
                sb.Append(Q(r.Type)); sb.Append(',');
                sb.Append(Q(r.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                sb.Append('\n');
            }
            await File.WriteAllTextAsync(tempFile, sb.ToString(), Encoding.UTF8, ct);

            var csb = new MySqlConnectionStringBuilder(ConnStr);
            bool allowLocalInfile = _config.GetValue<bool>("Import:UseLocalInfile", false) && csb.AllowLoadLocalInfile;
            int maxAttempts = _config.GetValue<int>("Import:MaxAttempts", 3);

            using var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(ct);

            // Attempt 1: MySqlBulkLoader
            if (allowLocalInfile)
            {
                var loader = new MySqlBulkLoader(conn)
                {
                    TableName = "AccountingData",
                    FileName = tempFile,
                    FieldTerminator = ",",
                    FieldQuotationCharacter = '"',
                    LineTerminator = "\n",
                    NumberOfLinesToSkip = 0,
                    Local = true
                };
                loader.Columns.AddRange(new[] { "AccountingClass","Date","Num","Amount","AccountNumber","Account","Type","DateCreated" });

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        result.Inserted = await Task.Run(() => (int)loader.Load(), ct);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BulkLoader accounting attempt {A} failed", attempt);
                        if (attempt < maxAttempts) await Task.Delay(250 * attempt, ct);
                    }
                }
            }

            // Attempt 2: multi-row parameterised INSERT
            var cols = new[] { "AccountingClass","Date","Num","Amount","AccountNumber","Account","Type","DateCreated" };

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var tx = await conn.BeginTransactionAsync(ct);
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    var valueFragments = new List<string>(batch.Count);
                    int pi = 0;
                    foreach (var row in batch)
                    {
                        var pn = Enumerable.Range(pi, cols.Length).Select(i => "@p" + i).ToArray();
                        pi += cols.Length;
                        valueFragments.Add("(" + string.Join(',', pn) + ")");
                        cmd.Parameters.Add(new MySqlParameter(pn[0], row.AccountingClass));
                        cmd.Parameters.Add(new MySqlParameter(pn[1], row.Date));
                        cmd.Parameters.Add(new MySqlParameter(pn[2], row.Num));
                        cmd.Parameters.Add(new MySqlParameter(pn[3], row.Amount));
                        cmd.Parameters.Add(new MySqlParameter(pn[4], row.AccountNumber));
                        cmd.Parameters.Add(new MySqlParameter(pn[5], row.Account));
                        cmd.Parameters.Add(new MySqlParameter(pn[6], row.Type));
                        cmd.Parameters.Add(new MySqlParameter(pn[7], row.DateCreated));
                    }
                    cmd.CommandText = $"INSERT INTO AccountingData ({string.Join(',', cols)}) VALUES {string.Join(',', valueFragments)}";
                    result.Inserted = await cmd.ExecuteNonQueryAsync(ct);
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Multi-row accounting insert attempt {A} failed", attempt);
                    if (attempt < maxAttempts) await Task.Delay(250 * attempt, ct);
                }
            }

            // Attempt 3: row-by-row fallback
            const string sql = @"INSERT INTO AccountingData
                (AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated)
                VALUES(@AccountingClass,@Date,@Num,@Amount,@AccountNumber,@Account,@Type,@DateCreated)";
            foreach (var row in batch)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.Add(new MySqlParameter("@AccountingClass", row.AccountingClass));
                    cmd.Parameters.Add(new MySqlParameter("@Date", row.Date));
                    cmd.Parameters.Add(new MySqlParameter("@Num", row.Num));
                    cmd.Parameters.Add(new MySqlParameter("@Amount", row.Amount));
                    cmd.Parameters.Add(new MySqlParameter("@AccountNumber", row.AccountNumber));
                    cmd.Parameters.Add(new MySqlParameter("@Account", row.Account));
                    cmd.Parameters.Add(new MySqlParameter("@Type", row.Type));
                    cmd.Parameters.Add(new MySqlParameter("@DateCreated", row.DateCreated));
                    if (await cmd.ExecuteNonQueryAsync(ct) > 0) result.Inserted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Row-by-row accounting insert failed");
                    result.Errors.Add(ex.Message);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AccountingImportRepository.BulkInsertAsync failed");
            result.Errors.Add(ex.Message);
            return result;
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    private async Task EnsureBackupTableAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
    {
        var create = conn.CreateCommand();
        create.Transaction = tx;
        create.CommandTimeout = 60;
        create.CommandText = @"
            CREATE TABLE IF NOT EXISTS AccountingDataBackup (
                BackupId CHAR(36) NOT NULL,
                Id INT NOT NULL,
                AccountingClass VARCHAR(255) NULL,
                Date DATETIME NULL,
                Num VARCHAR(255) NULL,
                Amount DECIMAL(18,2) NULL,
                AccountNumber VARCHAR(255) NULL,
                Account VARCHAR(255) NULL,
                Type VARCHAR(255) NULL,
                DateCreated DATETIME NULL,
                BackupAt DATETIME NOT NULL,
                Pinned TINYINT(1) NOT NULL DEFAULT 0,
                SourceRangeStart DATETIME NULL,
                PRIMARY KEY (BackupId, Id),
                KEY idx_acb_BackupAt (BackupAt),
                KEY idx_acb_Pinned (Pinned),
                KEY idx_acb_BackupId (BackupId)
            ) ENGINE=InnoDB";
        await create.ExecuteNonQueryAsync(ct);

        try
        {
            var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = @"SELECT EXTRA FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='AccountingDataBackup' AND COLUMN_NAME='Id'";
            var extra = await check.ExecuteScalarAsync(ct) as string;
            if (!string.IsNullOrEmpty(extra) && extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("AccountingDataBackup has AUTO_INCREMENT on Id — recreating");
                var drop = conn.CreateCommand(); drop.Transaction = tx;
                drop.CommandText = "DROP TABLE IF EXISTS AccountingDataBackup";
                await drop.ExecuteNonQueryAsync(ct);
                await create.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not verify AccountingDataBackup schema"); }
    }

    private async Task CleanupBackupsAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
    {
        foreach (var (dateFilter, alias) in new[] { ("= CURDATE()", "x"), ("< CURDATE()", "y") })
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
                DELETE FROM AccountingDataBackup
                WHERE Pinned=0 AND DATE(BackupAt) {dateFilter}
                  AND BackupId <> (
                      SELECT keepId FROM (
                          SELECT BackupId AS keepId FROM AccountingDataBackup
                          WHERE Pinned=0 AND DATE(BackupAt) {dateFilter}
                          GROUP BY BackupId ORDER BY MAX(BackupAt) DESC LIMIT 1
                      ) AS {alias})";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
