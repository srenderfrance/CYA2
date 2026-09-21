using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cya2.Infrastructure.Services
{
    public sealed class RollbackExecutor : IRollbackExecutor
    {
        private readonly IConfiguration _config;
        private readonly ILogger<RollbackExecutor> _logger;
        private readonly IRollbackRepository _rollbackRepository;

        public RollbackExecutor(
            IConfiguration config,
            ILogger<RollbackExecutor> logger,
            IRollbackRepository rollbackRepository)
        {
            _config = config;
            _logger = logger;
            _rollbackRepository = rollbackRepository;
        }

        public Task<RollbackResult> RollbackDonationsAsync(CancellationToken cancellationToken = default)
            => RollbackDonationsCoreAsync(cancellationToken);

        public Task<RollbackResult> RollbackAccountingAsync(CancellationToken cancellationToken = default)
            => RollbackAccountingCoreAsync(cancellationToken);

        private async Task<RollbackResult> RollbackDonationsCoreAsync(CancellationToken cancellationToken)
        {
            var result = new RollbackResult();
            var connStr = _config.GetConnectionString("default") ?? string.Empty;

            try
            {
                var csb = new MySqlConnectionStringBuilder(connStr);
                csb["Connection Timeout"] = 60;
                csb.DefaultCommandTimeout = 300;

                await using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync(cancellationToken);
                await using var tx = await conn.BeginTransactionAsync(cancellationToken);

                try
                {
                    static async Task<bool> ColumnExistsAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string column, CancellationToken ct)
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"SELECT COUNT(*)
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName";
                        cmd.Parameters.Add(new MySqlParameter("@TableName", table));
                        cmd.Parameters.Add(new MySqlParameter("@ColumnName", column));
                        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
                    }

                    static async Task EnsureColumnExistsAsync(MySqlConnection connection, MySqlTransaction transaction, string table, string column, string sqlType, CancellationToken ct)
                    {
                        if (await ColumnExistsAsync(connection, transaction, table, column, ct))
                        {
                            return;
                        }

                        var alter = connection.CreateCommand();
                        alter.Transaction = transaction;
                        alter.CommandTimeout = 60;
                        alter.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {sqlType} NULL";
                        await alter.ExecuteNonQueryAsync(ct);
                    }

                    // Check if the backup payload and snapshot metadata tables exist.
                    var checkBackupCmd = conn.CreateCommand();
                    checkBackupCmd.Transaction = (MySqlTransaction)tx;
                    checkBackupCmd.CommandText = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DonationDataBackup'";
                    
                    var tableExists = Convert.ToInt32(await checkBackupCmd.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    if (!tableExists)
                    {
                        result.Success = false;
                        result.ErrorMessage = "No donation backup table found. Cannot rollback.";
                        return result;
                    }

                    var snapshotTableExists = Convert.ToInt32(await ExecuteScalarAsync(conn, (MySqlTransaction)tx,
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DonationBackupSnapshots'",
                        cancellationToken)) > 0;
                    if (!snapshotTableExists)
                    {
                        result.Success = false;
                        result.ErrorMessage = "No donation backup snapshot metadata found. A newer upload is required for rollback.";
                        return result;
                    }

                    var donorBackupExists = Convert.ToInt32(await ExecuteScalarAsync(conn, (MySqlTransaction)tx,
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DonorsBackup'",
                        cancellationToken)) > 0;
                    var contactBackupExists = Convert.ToInt32(await ExecuteScalarAsync(conn, (MySqlTransaction)tx,
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DonorContactsBackup'",
                        cancellationToken)) > 0;
                    if (!donorBackupExists || !contactBackupExists)
                    {
                        result.Success = false;
                        result.ErrorMessage = "The selected donation backup does not contain donor and contact snapshots. A newer backup is required for canonical rollback.";
                        return result;
                    }

                    // Find the most recent non-pinned backup
                    var getLatestBackupCmd = conn.CreateCommand();
                    getLatestBackupCmd.Transaction = (MySqlTransaction)tx;
                    getLatestBackupCmd.CommandText = @"
                        SELECT BackupId, BackupAt, SourceRangeStart, RecordCount
                        FROM DonationBackupSnapshots
                        WHERE Pinned = 0
                        ORDER BY BackupAt DESC
                        LIMIT 1";

                    string? latestBackupId = null;
                    DateTime? backupDate = null;
                    DateTime? sourceRangeStart = null;
                    int backupRecordCount = 0;

                    using var reader = await getLatestBackupCmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync())
                    {
                        latestBackupId = reader.GetValue(0) switch
                        {
                            Guid value => value.ToString(),
                            string value => value,
                            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                        };
                        backupDate = reader.GetDateTime(1);
                        sourceRangeStart = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                        backupRecordCount = reader.GetInt32(3);
                    }
                    reader.Close();

                    if (string.IsNullOrEmpty(latestBackupId))
                    {
                        result.Success = false;
                        result.ErrorMessage = "No recent backup found for donations. Cannot rollback.";
                        return result;
                    }

                    _logger.LogInformation("Rolling back donations to backup {BackupId} from {BackupDate} with {RecordCount} records", 
                        latestBackupId, backupDate, backupRecordCount);

                    var backupTransaction = (MySqlTransaction)tx;

                    // Remove only the donation range replaced by the import.
                    var clearCmd = conn.CreateCommand();
                    clearCmd.Transaction = backupTransaction;
                    clearCmd.CommandTimeout = 300;
                    clearCmd.CommandText = "DELETE FROM DonationData WHERE Date >= @SourceRangeStart";
                    clearCmd.Parameters.AddWithValue("@SourceRangeStart", sourceRangeStart.Value);
                    var deletedRows = await clearCmd.ExecuteNonQueryAsync(cancellationToken);

                    var restoreDonors = conn.CreateCommand();
                    restoreDonors.Transaction = backupTransaction;
                    restoreDonors.CommandTimeout = 300;
                    restoreDonors.CommandText = @"
UPDATE Donors donor
INNER JOIN DonorsBackup backup
    ON backup.BackupId = @BackupId
   AND backup.Id = donor.Id
SET donor.Fund = backup.Fund,
    donor.DisplayName = backup.DisplayName,
    donor.IdentityKey = backup.IdentityKey,
    donor.ResolutionSource = backup.ResolutionSource,
    donor.ResolutionVersion = backup.ResolutionVersion,
    donor.DateCreated = backup.DateCreated,
    donor.DateModified = backup.DateModified";
                    restoreDonors.Parameters.AddWithValue("@BackupId", latestBackupId);
                    await restoreDonors.ExecuteNonQueryAsync(cancellationToken);

                    var insertMissingDonors = conn.CreateCommand();
                    insertMissingDonors.Transaction = backupTransaction;
                    insertMissingDonors.CommandTimeout = 300;
                    insertMissingDonors.CommandText = @"
INSERT INTO Donors
    (Id, Fund, DisplayName, IdentityKey, ResolutionSource, ResolutionVersion, DateCreated, DateModified)
SELECT backup.Id, backup.Fund, backup.DisplayName, backup.IdentityKey,
       backup.ResolutionSource, backup.ResolutionVersion, backup.DateCreated, backup.DateModified
FROM DonorsBackup backup
LEFT JOIN Donors donor ON donor.Id = backup.Id
WHERE backup.BackupId = @BackupId
  AND donor.Id IS NULL";
                    insertMissingDonors.Parameters.AddWithValue("@BackupId", latestBackupId);
                    await insertMissingDonors.ExecuteNonQueryAsync(cancellationToken);

                    var deleteCurrentContacts = conn.CreateCommand();
                    deleteCurrentContacts.Transaction = backupTransaction;
                    deleteCurrentContacts.CommandTimeout = 300;
                    deleteCurrentContacts.CommandText = @"
DELETE contact
FROM DonorContacts contact
INNER JOIN DonorsBackup backup
    ON backup.BackupId = @BackupId
   AND backup.Id = contact.DonorId";
                    deleteCurrentContacts.Parameters.AddWithValue("@BackupId", latestBackupId);
                    await deleteCurrentContacts.ExecuteNonQueryAsync(cancellationToken);

                    var restoreContacts = conn.CreateCommand();
                    restoreContacts.Transaction = backupTransaction;
                    restoreContacts.CommandTimeout = 300;
                    restoreContacts.CommandText = @"
INSERT INTO DonorContacts
    (Id, DonorId, ContactType, ContactValue, NormalizedValue, DateCreated, DateModified)
SELECT backup.Id, backup.DonorId, backup.ContactType, backup.ContactValue,
       backup.NormalizedValue, backup.DateCreated, backup.DateModified
FROM DonorContactsBackup backup
WHERE backup.BackupId = @BackupId";
                    restoreContacts.Parameters.AddWithValue("@BackupId", latestBackupId);
                    await restoreContacts.ExecuteNonQueryAsync(cancellationToken);

                    // Restore from backup
                    var restoreCmd = conn.CreateCommand();
                    restoreCmd.Transaction = backupTransaction;
                    restoreCmd.CommandTimeout = 300;
                    restoreCmd.CommandText = $@"
                        INSERT INTO DonationData
                        (Id, DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType,
                         Amount, Fund, Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName,
                         IsAnonymous, Frequency, DateCreated)
                         SELECT Id, DonorId, Date, GiftImportId, AccountName, PaymentMethod, GiftType,
                                Amount, Fund, Intern, PrimaryAddressee, SoftCreditName, HonorMemorialName,
                                COALESCE(IsAnonymous, 0), Frequency, DateCreated
                         FROM DonationDataBackup
                         WHERE BackupId = @BackupId
                           AND Id <> 0";
                    restoreCmd.Parameters.Add(new MySqlParameter("@BackupId", latestBackupId));
                    
                    var restoredRows = await restoreCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Remove donors created by the rolled-back import when no donation references them.
                    var orphanDonors = conn.CreateCommand();
                    orphanDonors.Transaction = backupTransaction;
                    orphanDonors.CommandText = @"
DELETE donor FROM Donors donor
LEFT JOIN DonationData donation ON donation.DonorId = donor.Id
WHERE donation.Id IS NULL
  AND NOT EXISTS (SELECT 1 FROM DonorsBackup backup WHERE backup.BackupId = @BackupId AND backup.Id = donor.Id)";
                    orphanDonors.Parameters.AddWithValue("@BackupId", latestBackupId);
                    await orphanDonors.ExecuteNonQueryAsync(cancellationToken);

                    // Pin this backup to prevent it from being deleted
                    var pinCmd = conn.CreateCommand();
                    pinCmd.Transaction = (MySqlTransaction)tx;
                     pinCmd.CommandText = @"
                         UPDATE DonationBackupSnapshots
                        SET Pinned = 1 
                        WHERE BackupId = @BackupId";
                    pinCmd.Parameters.Add(new MySqlParameter("@BackupId", latestBackupId));
                    await pinCmd.ExecuteNonQueryAsync(cancellationToken);

                    await tx.CommitAsync(cancellationToken);

                    result.Success = true;
                    result.DonationRowsRestored = restoredRows;
                    result.Message = $"Successfully restored {restoredRows:N0} donation records from backup dated {backupDate:yyyy-MM-dd HH:mm}";
                    
                    _logger.LogInformation("Donation rollback completed successfully. Restored {RestoredRows} rows, deleted {DeletedRows} rows", 
                        restoredRows, deletedRows);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during donation rollback");
                result.Success = false;
                result.ErrorMessage = $"Donation rollback failed: {ex.Message}";
            }

            return result;
        }

        private async Task<RollbackResult> RollbackAccountingCoreAsync(CancellationToken cancellationToken)
        {
            var result = new RollbackResult();
            var connStr = _config.GetConnectionString("default") ?? string.Empty;

            try
            {
                var csb = new MySqlConnectionStringBuilder(connStr);
                csb["Connection Timeout"] = 60;
                csb.DefaultCommandTimeout = 300;

                await using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync(cancellationToken);
                await using var tx = await conn.BeginTransactionAsync(cancellationToken);

                try
                {
                    // Check if backup table exists and has data
                    var checkBackupCmd = conn.CreateCommand();
                    checkBackupCmd.Transaction = (MySqlTransaction)tx;
                    checkBackupCmd.CommandText = @"
                        SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AccountingDataBackup'";
                    
                    var tableExists = Convert.ToInt32(await checkBackupCmd.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    if (!tableExists)
                    {
                        result.Success = false;
                        result.ErrorMessage = "No accounting backup table found. Cannot rollback.";
                        return result;
                    }

                    var snapshotTableExists = Convert.ToInt32(await ExecuteScalarAsync(conn, (MySqlTransaction)tx,
                        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'AccountingBackupSnapshots'",
                        cancellationToken)) > 0;
                    if (!snapshotTableExists)
                    {
                        result.Success = false;
                        result.ErrorMessage = "No accounting backup snapshot metadata found. A newer upload is required for rollback.";
                        return result;
                    }

                    // Find the most recent non-pinned backup
                    var getLatestBackupCmd = conn.CreateCommand();
                    getLatestBackupCmd.Transaction = (MySqlTransaction)tx;
                    getLatestBackupCmd.CommandText = @"
                        SELECT BackupId, BackupAt, SourceRangeStart, RecordCount
                        FROM AccountingBackupSnapshots
                        WHERE Pinned = 0
                        ORDER BY BackupAt DESC
                        LIMIT 1";

                    string? latestBackupId = null;
                    DateTime? backupDate = null;
                    DateTime? sourceRangeStart = null;
                    int backupRecordCount = 0;

                    using var reader = await getLatestBackupCmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync())
                    {
                        latestBackupId = reader.GetString(0);
                        backupDate = reader.GetDateTime(1);
                        sourceRangeStart = reader.GetDateTime(2);
                        backupRecordCount = reader.GetInt32(3);
                    }
                    reader.Close();

                    if (string.IsNullOrEmpty(latestBackupId))
                    {
                        result.Success = false;
                        result.ErrorMessage = "No recent backup found for accounting data. Cannot rollback.";
                        return result;
                    }

                    _logger.LogInformation("Rolling back accounting data to backup {BackupId} from {BackupDate} with {RecordCount} records", 
                        latestBackupId, backupDate, backupRecordCount);

                    // Clear current accounting data
                    var clearCmd = conn.CreateCommand();
                    clearCmd.Transaction = (MySqlTransaction)tx;
                    clearCmd.CommandTimeout = 300;
                    clearCmd.CommandText = "DELETE FROM AccountingData";
                    var deletedRows = await clearCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Restore from backup
                    var restoreCmd = conn.CreateCommand();
                    restoreCmd.Transaction = (MySqlTransaction)tx;
                    restoreCmd.CommandTimeout = 300;
                    restoreCmd.CommandText = @"
                        INSERT INTO AccountingData 
                        (Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated)
                        SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
                        FROM AccountingDataBackup 
                        WHERE BackupId = @BackupId";
                    restoreCmd.Parameters.Add(new MySqlParameter("@BackupId", latestBackupId));
                    
                    var restoredRows = await restoreCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Pin this backup to prevent it from being deleted
                    var pinCmd = conn.CreateCommand();
                    pinCmd.Transaction = (MySqlTransaction)tx;
                    pinCmd.CommandText = @"
                        UPDATE AccountingBackupSnapshots
                        SET Pinned = 1 
                        WHERE BackupId = @BackupId";
                    pinCmd.Parameters.Add(new MySqlParameter("@BackupId", latestBackupId));
                    await pinCmd.ExecuteNonQueryAsync(cancellationToken);

                    await tx.CommitAsync(cancellationToken);

                    result.Success = true;
                    result.AccountingRowsRestored = restoredRows;
                    result.Message = $"Successfully restored {restoredRows:N0} accounting records from backup dated {backupDate:yyyy-MM-dd HH:mm}";
                    
                    _logger.LogInformation("Accounting rollback completed successfully. Restored {RestoredRows} rows, deleted {DeletedRows} rows", 
                        restoredRows, deletedRows);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during accounting rollback");
                result.Success = false;
                result.ErrorMessage = $"Accounting rollback failed: {ex.Message}";
            }

            return result;
        }

        private static MySqlCommand CreateCommand(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string commandText,
            int? commandTimeout = null)
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = commandText;
            if (commandTimeout.HasValue)
            {
                cmd.CommandTimeout = commandTimeout.Value;
            }

            return cmd;
        }

        private static async Task<object?> ExecuteScalarAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string commandText,
            CancellationToken cancellationToken,
            int? commandTimeout = null)
        {
            using var cmd = CreateCommand(connection, transaction, commandText, commandTimeout);
            return await cmd.ExecuteScalarAsync(cancellationToken);
        }

        private static async Task<int> ExecuteNonQueryAsync(
            MySqlConnection connection,
            MySqlTransaction transaction,
            string commandText,
            CancellationToken cancellationToken,
            int? commandTimeout = null)
        {
            using var cmd = CreateCommand(connection, transaction, commandText, commandTimeout);
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// Gets information about available backups for rollback
        /// </summary>
        public async Task<RollbackAvailabilityInfo> GetRollbackAvailabilityAsync()
        {
            var info = new RollbackAvailabilityInfo();

            try
            {
                var donationBackups = await _rollbackRepository.GetAvailableDonationBackupsAsync();
                info.DonationBackupsAvailable = donationBackups.Any();
                info.LatestDonationBackup = donationBackups.Any()
                    ? new BackupInfo
                    {
                        BackupId = donationBackups[0].BackupId,
                        BackupAt = donationBackups[0].BackupAt,
                        RecordCount = donationBackups[0].RecordCount,
                        MostRecentDataDate = donationBackups[0].MostRecentDataDate
                    }
                    : null;

                var accountingBackups = await _rollbackRepository.GetAvailableAccountingBackupsAsync();
                info.AccountingBackupsAvailable = accountingBackups.Any();
                info.LatestAccountingBackup = accountingBackups.Any()
                    ? new BackupInfo
                    {
                        BackupId = accountingBackups[0].BackupId,
                        BackupAt = accountingBackups[0].BackupAt,
                        RecordCount = accountingBackups[0].RecordCount,
                        MostRecentDataDate = accountingBackups[0].MostRecentDataDate
                    }
                    : null;

                info.CanRollback = info.DonationBackupsAvailable || info.AccountingBackupsAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking rollback availability");
                info.ErrorMessage = $"Error checking backup availability: {ex.Message}";
            }

            return info;
        }
    }
}