using System.Globalization;
using System.Text;
using Cya2.Core.DTOs;
using Cya2.Core.Enums;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace Cya2.Infrastructure.Repositories;

public sealed class DonationImportRepository : IDonationImportRepository
{
    private readonly IConfiguration _config;
    private readonly ILogger<DonationImportRepository> _logger;
    private readonly IImportProgressService _progress;
    private readonly IDatabaseGuard _dbGuard;

    public DonationImportRepository(
        IConfiguration config,
        ILogger<DonationImportRepository> logger,
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
            countCmd.CommandText = "SELECT COUNT(*) FROM DonationData";
            var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

            if (rowCount > 0)
            {
                _progress.UpdateStep(progressId, "Database Backup", $"Backing up all {rowCount:N0} existing records...");

                bool hasIsAnon = await ColumnExistsAsync(conn, (MySqlTransaction)tx, "DonationData", "IsAnonymous", ct);

                var insert = conn.CreateCommand();
                insert.Transaction = (MySqlTransaction)tx;
                insert.CommandTimeout = 300;
                insert.CommandText = hasIsAnon
                    ? @"INSERT INTO DonationDataBackup
                        (Id,Date,AccountName,PaymentMethod,GiftType,Amount,Fund,SoftCreditName,Address,City,State,PostalCode,Country,Email,PhoneFixed,PhoneMobile,DateCreated,IsAnonymous,BackupId,BackupAt,Pinned,SourceRangeStart)
                        SELECT Id,Date,AccountName,PaymentMethod,GiftType,Amount,Fund,SoftCreditName,Address,City,State,PostalCode,Country,Email,PhoneFixed,PhoneMobile,DateCreated,IsAnonymous,@bid,UTC_TIMESTAMP(),0,@from
                        FROM DonationData"
                    : @"INSERT INTO DonationDataBackup
                        (Id,Date,AccountName,PaymentMethod,GiftType,Amount,Fund,SoftCreditName,Address,City,State,PostalCode,Country,Email,PhoneFixed,PhoneMobile,DateCreated,BackupId,BackupAt,Pinned,SourceRangeStart)
                        SELECT Id,Date,AccountName,PaymentMethod,GiftType,Amount,Fund,SoftCreditName,Address,City,State,PostalCode,Country,Email,PhoneFixed,PhoneMobile,DateCreated,@bid,UTC_TIMESTAMP(),0,@from
                        FROM DonationData";
                insert.Parameters.Add(new MySqlParameter("@bid", backupId));
                insert.Parameters.Add(new MySqlParameter("@from", fromDate));
                await insert.ExecuteNonQueryAsync(ct);

                _progress.UpdateStep(progressId, "Database Backup", $"Removing records from {fromDate:yyyy-MM-dd} forward...");
                var del = conn.CreateCommand();
                del.Transaction = (MySqlTransaction)tx;
                del.CommandTimeout = 300;
                del.CommandText = "DELETE FROM DonationData WHERE Date >= @from";
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
            _logger.LogError(ex, "Donation backup/delete failed");
            _progress.UpdateStep(progressId, "Database Backup", $"Failed: {ex.Message}");
            throw;
        }
    }

    public async Task<int> RecategorizeAllDonationsAsync(CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();

        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var loadCmd = conn.CreateCommand();
            loadCmd.Transaction = tx;
            loadCmd.CommandTimeout = 300;
            loadCmd.CommandText = @"
SELECT Id, AccountName, Fund, Date, Amount
FROM DonationData
ORDER BY AccountName, Fund, Date, Id";

            var records = new List<(int Id, string AccountName, string Fund, DateTime Date, decimal Amount)>();
            await using (var reader = await loadCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    var id = reader.GetInt32(reader.GetOrdinal("Id"));
                    var accountName = reader.IsDBNull(reader.GetOrdinal("AccountName")) ? string.Empty : reader.GetString(reader.GetOrdinal("AccountName"));
                    var fund = reader.IsDBNull(reader.GetOrdinal("Fund")) ? string.Empty : reader.GetString(reader.GetOrdinal("Fund"));
                    var date = reader.GetDateTime(reader.GetOrdinal("Date"));
                    var amount = Convert.ToDecimal(reader.GetDouble(reader.GetOrdinal("Amount")));
                    records.Add((id, accountName, fund, date, amount));
                }
            }

            var frequencyService = new DonorFrequencyService();
            var updates = new List<(int Id, int Frequency)>();

            // Recategorize the ENTIRE table.
            // For rows with missing identifiers, use safe fallback grouping keys so no row is skipped.
            // - Missing AccountName -> isolate by row Id (prevents unrelated blanks from being mixed)
            // - Missing Fund -> group under a shared sentinel for that donor
            var groups = records
                .GroupBy(r => new
                {
                    AccountNameKey = string.IsNullOrWhiteSpace(r.AccountName)
                        ? $"__ROW__{r.Id}"
                        : r.AccountName.Trim().ToUpperInvariant(),
                    FundKey = string.IsNullOrWhiteSpace(r.Fund)
                        ? "__NO_FUND__"
                        : r.Fund.Trim().ToUpperInvariant()
                });

            foreach (var group in groups)
            {
                var sorted = group.OrderBy(g => g.Date).ThenBy(g => g.Id).ToList();
                var history = sorted
                    .Select(g => new DonorGiftRecord { Date = g.Date, Amount = g.Amount })
                    .ToList();

                for (int i = 0; i < sorted.Count; i++)
                {
                    var gift = history[i];
                    var classification = frequencyService.ClassifyGift(gift, history);
                    var freq = classification.Frequency == DonorFrequency.None
                        ? DonorFrequency.OneTime
                        : classification.Frequency;
                    updates.Add((sorted[i].Id, (int)freq));
                }
            }

            // Ensure every row in DonationData is included in the recategorization set.
            var allIds = records.Select(r => r.Id).OrderBy(i => i).ToList();
            var updateIds = updates.Select(u => u.Id).OrderBy(i => i).ToList();
            if (allIds.Count != updateIds.Count)
            {
                throw new InvalidOperationException($"Recategorization did not cover entire table. Rows={allIds.Count}, categorized={updateIds.Count}.");
            }

            var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandTimeout = 300;
            updateCmd.CommandText = "UPDATE DonationData SET Frequency = @freq WHERE Id = @id";
            updateCmd.Parameters.Add(new MySqlParameter("@freq", MySqlDbType.Int32));
            updateCmd.Parameters.Add(new MySqlParameter("@id", MySqlDbType.Int32));

            int updated = 0;
            foreach (var u in updates)
            {
                updateCmd.Parameters["@freq"].Value = u.Frequency;
                updateCmd.Parameters["@id"].Value = u.Id;
                updated += await updateCmd.ExecuteNonQueryAsync(ct);
            }

            // Hard validation: no NULL or out-of-range values after recategorization.
            var validateCmd = conn.CreateCommand();
            validateCmd.Transaction = tx;
            validateCmd.CommandTimeout = 120;
            validateCmd.CommandText = @"
SELECT COUNT(*)
FROM DonationData
WHERE Frequency IS NULL OR Frequency NOT IN (1,2,3,4)";
            var invalidCount = Convert.ToInt32(await validateCmd.ExecuteScalarAsync(ct));
            if (invalidCount > 0)
            {
                throw new InvalidOperationException($"Recategorization left {invalidCount} invalid Frequency rows.");
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("Recategorized all donations. Rows updated={Rows}", updated);
            return updated;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<ImportBatchResult> BulkInsertAsync(IReadOnlyList<DonationImportRowDto> batch, CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();

        var result = new ImportBatchResult();
        if (batch == null || batch.Count == 0) return result;

        string tempFile = Path.Combine(Path.GetTempPath(), $"donation_import_{Guid.NewGuid():N}.csv");
        try
        {
            var sb = new StringBuilder();
            foreach (var r in batch)
            {
                static string Q(string? s) => s is null ? "" : '"' + s.Replace("\"", "\"\"") + '"';
                sb.Append(Q(r.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))); sb.Append(',');
                sb.Append(Q(r.AccountName)); sb.Append(',');
                sb.Append(Q(r.PaymentMethod)); sb.Append(',');
                sb.Append(Q(r.GiftType)); sb.Append(',');
                sb.Append(Q(r.Amount.ToString(CultureInfo.InvariantCulture))); sb.Append(',');
                sb.Append(Q(r.Fund)); sb.Append(',');
                sb.Append(Q(r.SoftCreditName)); sb.Append(',');
                sb.Append(Q(r.Address)); sb.Append(',');
                sb.Append(Q(r.City)); sb.Append(',');
                sb.Append(Q(r.State)); sb.Append(',');
                sb.Append(Q(r.PostalCode)); sb.Append(',');
                sb.Append(Q(r.Country)); sb.Append(',');
                sb.Append(Q(r.Email)); sb.Append(',');
                sb.Append(Q(r.PhoneFixed)); sb.Append(',');
                sb.Append(Q(r.PhoneMobile)); sb.Append(',');
                sb.Append(Q(r.IsAnonymous ? "1" : "0")); sb.Append(',');
                sb.Append(Q(r.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))); sb.Append(',');
                sb.Append(r.Frequency.HasValue ? ((int)r.Frequency.Value).ToString() : "");
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
                    TableName = "DonationData",
                    FileName = tempFile,
                    FieldTerminator = ",",
                    FieldQuotationCharacter = '"',
                    LineTerminator = "\n",
                    NumberOfLinesToSkip = 0,
                    Local = true
                };
                loader.Columns.AddRange(new[] { "Date","AccountName","PaymentMethod","GiftType","Amount","Fund",
                    "SoftCreditName","Address","City","State","PostalCode","Country","Email",
                    "PhoneFixed","PhoneMobile","IsAnonymous","DateCreated","Frequency" });

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        result.Inserted = await Task.Run(() => (int)loader.Load(), ct);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BulkLoader donation attempt {A} failed", attempt);
                        if (attempt < maxAttempts) await Task.Delay(250 * attempt, ct);
                    }
                }
            }

            // Attempt 2: multi-row parameterised INSERT
            var cols = new[] { "Date","AccountName","PaymentMethod","GiftType","Amount","Fund",
                "SoftCreditName","Address","City","State","PostalCode","Country","Email",
                "PhoneFixed","PhoneMobile","IsAnonymous","DateCreated","Frequency" };

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
                        cmd.Parameters.Add(new MySqlParameter(pn[0], row.Date));
                        cmd.Parameters.Add(new MySqlParameter(pn[1], row.AccountName));
                        cmd.Parameters.Add(new MySqlParameter(pn[2], row.PaymentMethod));
                        cmd.Parameters.Add(new MySqlParameter(pn[3], row.GiftType));
                        cmd.Parameters.Add(new MySqlParameter(pn[4], row.Amount));
                        cmd.Parameters.Add(new MySqlParameter(pn[5], row.Fund));
                        cmd.Parameters.Add(new MySqlParameter(pn[6], (object?)row.SoftCreditName ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[7], (object?)row.Address ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[8], (object?)row.City ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[9], (object?)row.State ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[10], (object?)row.PostalCode ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[11], (object?)row.Country ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[12], (object?)row.Email ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[13], (object?)row.PhoneFixed ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[14], (object?)row.PhoneMobile ?? DBNull.Value));
                        cmd.Parameters.Add(new MySqlParameter(pn[15], row.IsAnonymous ? 1 : 0));
                        cmd.Parameters.Add(new MySqlParameter(pn[16], row.DateCreated));
                        cmd.Parameters.Add(new MySqlParameter(pn[17], row.Frequency.HasValue ? (object)(int)row.Frequency.Value : DBNull.Value));
                    }
                    cmd.CommandText = $"INSERT INTO DonationData ({string.Join(',', cols)}) VALUES {string.Join(',', valueFragments)}";
                    result.Inserted = await cmd.ExecuteNonQueryAsync(ct);
                    await tx.CommitAsync(ct);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Multi-row donation insert attempt {A} failed", attempt);
                    if (attempt < maxAttempts) await Task.Delay(250 * attempt, ct);
                }
            }

            // Attempt 3: row-by-row fallback
            const string sql = @"INSERT INTO DonationData
                (Date,AccountName,PaymentMethod,GiftType,Amount,Fund,SoftCreditName,Address,City,State,PostalCode,Country,Email,PhoneFixed,PhoneMobile,IsAnonymous,DateCreated,Frequency)
                VALUES(@Date,@AccountName,@PaymentMethod,@GiftType,@Amount,@Fund,@SoftCreditName,@Address,@City,@State,@PostalCode,@Country,@Email,@PhoneFixed,@PhoneMobile,@IsAnonymous,@DateCreated,@Frequency)";
            foreach (var row in batch)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.Parameters.Add(new MySqlParameter("@Date", row.Date));
                    cmd.Parameters.Add(new MySqlParameter("@AccountName", row.AccountName));
                    cmd.Parameters.Add(new MySqlParameter("@PaymentMethod", row.PaymentMethod));
                    cmd.Parameters.Add(new MySqlParameter("@GiftType", row.GiftType));
                    cmd.Parameters.Add(new MySqlParameter("@Amount", row.Amount));
                    cmd.Parameters.Add(new MySqlParameter("@Fund", row.Fund));
                    cmd.Parameters.Add(new MySqlParameter("@SoftCreditName", (object?)row.SoftCreditName ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@Address", (object?)row.Address ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@City", (object?)row.City ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@State", (object?)row.State ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@PostalCode", (object?)row.PostalCode ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@Country", (object?)row.Country ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@Email", (object?)row.Email ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@PhoneFixed", (object?)row.PhoneFixed ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@PhoneMobile", (object?)row.PhoneMobile ?? DBNull.Value));
                    cmd.Parameters.Add(new MySqlParameter("@IsAnonymous", row.IsAnonymous ? 1 : 0));
                    cmd.Parameters.Add(new MySqlParameter("@DateCreated", row.DateCreated));
                    cmd.Parameters.Add(new MySqlParameter("@Frequency", row.Frequency.HasValue ? (object)(int)row.Frequency.Value : DBNull.Value));
                    if (await cmd.ExecuteNonQueryAsync(ct) > 0) result.Inserted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Row-by-row donation insert failed");
                    result.Errors.Add(ex.Message);
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DonationImportRepository.BulkInsertAsync failed");
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
            CREATE TABLE IF NOT EXISTS DonationDataBackup (
                BackupId CHAR(36) NOT NULL,
                Id INT NOT NULL,
                Date DATETIME NULL,
                AccountName VARCHAR(255) NULL,
                PaymentMethod VARCHAR(255) NULL,
                GiftType VARCHAR(255) NULL,
                Amount DECIMAL(18,2) NULL,
                Fund VARCHAR(255) NULL,
                SoftCreditName VARCHAR(255) NULL,
                Address VARCHAR(255) NULL,
                City VARCHAR(255) NULL,
                State VARCHAR(100) NULL,
                PostalCode VARCHAR(50) NULL,
                Country VARCHAR(100) NULL,
                Email VARCHAR(255) NULL,
                PhoneFixed VARCHAR(50) NULL,
                PhoneMobile VARCHAR(50) NULL,
                DateCreated DATETIME NULL,
                IsAnonymous TINYINT(1) NULL DEFAULT 0,
                BackupAt DATETIME NOT NULL,
                Pinned TINYINT(1) NOT NULL DEFAULT 0,
                SourceRangeStart DATETIME NULL,
                PRIMARY KEY (BackupId, Id),
                KEY idx_dbb_BackupAt (BackupAt),
                KEY idx_dbb_Pinned (Pinned),
                KEY idx_dbb_BackupId (BackupId)
            ) ENGINE=InnoDB";
        await create.ExecuteNonQueryAsync(ct);

        // Guard against legacy AUTO_INCREMENT schema
        try
        {
            var check = conn.CreateCommand();
            check.Transaction = tx;
            check.CommandText = @"SELECT EXTRA FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='DonationDataBackup' AND COLUMN_NAME='Id'";
            var extra = await check.ExecuteScalarAsync(ct) as string;
            if (!string.IsNullOrEmpty(extra) && extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("DonationDataBackup has AUTO_INCREMENT on Id — recreating");
                var drop = conn.CreateCommand(); drop.Transaction = tx;
                drop.CommandText = "DROP TABLE IF EXISTS DonationDataBackup";
                await drop.ExecuteNonQueryAsync(ct);
                await create.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not verify DonationDataBackup schema"); }
    }

    private async Task CleanupBackupsAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
    {
        foreach (var (dateFilter, alias) in new[] { ("= CURDATE()", "x"), ("< CURDATE()", "y") })
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandTimeout = 60;
            cmd.CommandText = $@"
                DELETE FROM DonationDataBackup
                WHERE Pinned=0 AND DATE(BackupAt) {dateFilter}
                  AND BackupId <> (
                      SELECT keepId FROM (
                          SELECT BackupId AS keepId FROM DonationDataBackup
                          WHERE Pinned=0 AND DATE(BackupAt) {dateFilter}
                          GROUP BY BackupId ORDER BY MAX(BackupAt) DESC LIMIT 1
                      ) AS {alias})";
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<bool> ColumnExistsAsync(MySqlConnection conn, MySqlTransaction tx, string table, string column, CancellationToken ct)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@t AND COLUMN_NAME=@c";
        cmd.Parameters.Add(new MySqlParameter("@t", table));
        cmd.Parameters.Add(new MySqlParameter("@c", column));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<List<DonationRecord>> GetRecentDonationsForDonorsAsync(
        IEnumerable<(string AccountName, string Fund)> donorKeys,
        DateTime beforeDate,
        int maxPerDonor,
        CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();

        var keys = donorKeys
            .Where(k => !string.IsNullOrWhiteSpace(k.AccountName) && !string.IsNullOrWhiteSpace(k.Fund))
            .Distinct()
            .ToList();

        if (keys.Count == 0) return new List<DonationRecord>();

        var results = new List<DonationRecord>();

        try
        {
            await using var conn = new MySqlConnection(ConnStr);
            await conn.OpenAsync(ct);

            // Build an IN-style query using a temporary values list.
            // We query the last maxPerDonor rows per (AccountName, Fund) pair before beforeDate.
            // This uses a ranked subquery compatible with MySQL 8+.
            var unionParts = new List<string>();
            var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 60;

            int pi = 0;
            foreach (var (accountName, fund) in keys)
            {
                var pName = $"@n{pi}";
                var pFund = $"@f{pi}";
                unionParts.Add($"(SELECT AccountName, Fund, Date, Amount, PaymentMethod, Frequency FROM DonationData WHERE AccountName={pName} AND Fund={pFund} AND Date < @beforeDate ORDER BY Date DESC LIMIT {maxPerDonor})");
                cmd.Parameters.Add(new MySqlParameter(pName, accountName));
                cmd.Parameters.Add(new MySqlParameter(pFund, fund));
                pi++;
            }

            cmd.Parameters.Add(new MySqlParameter("@beforeDate", beforeDate));
            cmd.CommandText = string.Join(" UNION ALL ", unionParts);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var freqOrdinal = reader.GetOrdinal("Frequency");
                var nameOrdinal = reader.GetOrdinal("AccountName");
                var fundOrdinal = reader.GetOrdinal("Fund");
                var dateOrdinal = reader.GetOrdinal("Date");
                var amountOrdinal = reader.GetOrdinal("Amount");
                var paymentOrdinal = reader.GetOrdinal("PaymentMethod");

                DonorFrequency? freq = reader.IsDBNull(freqOrdinal)
                    ? null
                    : (DonorFrequency)reader.GetInt32(freqOrdinal);

                results.Add(new DonationRecord
                {
                    AccountName   = reader.GetString(nameOrdinal),
                    Fund          = reader.GetString(fundOrdinal),
                    Date          = reader.GetDateTime(dateOrdinal),
                    Amount        = reader.GetDouble(amountOrdinal),
                    PaymentMethod = reader.IsDBNull(paymentOrdinal) ? string.Empty : reader.GetString(paymentOrdinal),
                    Frequency     = freq
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetRecentDonationsForDonorsAsync failed for {KeyCount} donor keys", keys.Count);
        }

        return results;
    }

    public async Task<(int donationDataUpdated, int donationBackupUpdated)> NormalizeExistingDonorNamesAsync(CancellationToken ct)
    {
        _dbGuard.ThrowIfUnavailable();

        await using var conn = new MySqlConnection(ConnStr);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            int RunUpdate(string tableName)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 120;
                cmd.CommandText = $@"
UPDATE `{tableName}`
SET AccountName = TRIM(CONCAT(TRIM(SUBSTRING_INDEX(AccountName, ',', -1)), ' ', TRIM(SUBSTRING_INDEX(AccountName, ',', 1))))
WHERE AccountName IS NOT NULL
  AND TRIM(AccountName) <> ''
  AND AccountName <> 'Anonymous'
  AND (LENGTH(AccountName) - LENGTH(REPLACE(AccountName, ',', ''))) = 1;";
                return cmd.ExecuteNonQuery();
            }

            var dataUpdated = RunUpdate("DonationData");
            var backupUpdated = RunUpdate("DonationDataBackup");

            await tx.CommitAsync(ct);
            _logger.LogInformation("Normalized donor names. DonationData={DonationRows}, DonationDataBackup={BackupRows}", dataUpdated, backupUpdated);
            return (dataUpdated, backupUpdated);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
