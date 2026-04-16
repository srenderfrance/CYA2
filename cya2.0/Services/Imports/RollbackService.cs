using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace cya2.Services.Imports
{
    public class RollbackService : IRollbackService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<RollbackService> _logger;
        private readonly IRollbackRepository _rollbackRepository;
        private readonly ISessionDashboardDtoCacheService _dashboardCache;
        private readonly IImportCacheInvalidator _cacheInvalidator;

        public RollbackService(
            IConfiguration config,
            ILogger<RollbackService> logger,
            IRollbackRepository rollbackRepository,
            ISessionDashboardDtoCacheService dashboardCache,
            IImportCacheInvalidator cacheInvalidator)
        {
            _config = config;
            _logger = logger;
            _rollbackRepository = rollbackRepository;
            _dashboardCache = dashboardCache;
            _cacheInvalidator = cacheInvalidator;
        }

        public async Task<RollbackResult> ExecuteRollbackAsync(string target, CancellationToken cancellationToken = default)
        {
            var result = new RollbackResult();

            try
            {
                switch (target.ToLowerInvariant())
                {
                    case "donations":
                        result = await RollbackDonationsAsync(cancellationToken);
                        break;
                    case "accounting":
                        result = await RollbackAccountingAsync(cancellationToken);
                        break;
                    case "both":
                        var donationResult = await RollbackDonationsAsync(cancellationToken);
                        var accountingResult = await RollbackAccountingAsync(cancellationToken);

                        result.Success = donationResult.Success && accountingResult.Success;
                        result.Message = $"Donations: {donationResult.Message}, Accounting: {accountingResult.Message}";
                        result.DonationRowsRestored = donationResult.DonationRowsRestored;
                        result.AccountingRowsRestored = accountingResult.AccountingRowsRestored;

                        if (!donationResult.Success || !accountingResult.Success)
                            result.ErrorMessage = $"Errors: {donationResult.ErrorMessage} {accountingResult.ErrorMessage}".Trim();
                        break;
                    default:
                        result.Success = false;
                        result.ErrorMessage = $"Invalid rollback target: {target}";
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing rollback for target: {Target}", target);
                result.Success = false;
                result.ErrorMessage = $"Rollback failed: {ex.Message}";
            }

            if (result.Success)
                _cacheInvalidator.InvalidateAll();

            return result;
        }

        private async Task<RollbackResult> RollbackDonationsAsync(CancellationToken cancellationToken)
        {
            var result = new RollbackResult();
            var connStr = _config.GetConnectionString("default") ?? string.Empty;

            try
            {
                // Build connection string with extended timeouts for rollback operations
                var csb = new MySqlConnectionStringBuilder(connStr)
                {
                    ConnectionTimeout = 60, // 1 minute for connection
                    DefaultCommandTimeout = 300 // 5 minutes for commands
                };

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
                        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DonationDataBackup'";
                    
                    var tableExists = Convert.ToInt32(await checkBackupCmd.ExecuteScalarAsync(cancellationToken)) > 0;
                    
                    if (!tableExists)
                    {
                        result.Success = false;
                        result.ErrorMessage = "No donation backup table found. Cannot rollback.";
                        return result;
                    }

                    // Find the most recent non-pinned backup
                    var getLatestBackupCmd = conn.CreateCommand();
                    getLatestBackupCmd.Transaction = (MySqlTransaction)tx;
                    getLatestBackupCmd.CommandText = @"
                        SELECT BackupId, BackupAt, COUNT(*) as RecordCount
                        FROM DonationDataBackup 
                        WHERE Pinned = 0
                        GROUP BY BackupId, BackupAt
                        ORDER BY BackupAt DESC
                        LIMIT 1";

                    string? latestBackupId = null;
                    DateTime? backupDate = null;
                    int backupRecordCount = 0;

                    using var reader = await getLatestBackupCmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync())
                    {
                        latestBackupId = reader.GetGuid(0).ToString();  // BackupId - convert GUID to string
                        backupDate = reader.GetDateTime(1);    // BackupAt  
                        backupRecordCount = reader.GetInt32(2); // RecordCount
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

                    // Clear current donation data
                    var clearCmd = conn.CreateCommand();
                    clearCmd.Transaction = (MySqlTransaction)tx;
                    clearCmd.CommandTimeout = 300;
                    clearCmd.CommandText = "DELETE FROM DonationData";
                    var deletedRows = await clearCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Restore from backup
                    var restoreCmd = conn.CreateCommand();
                    restoreCmd.Transaction = (MySqlTransaction)tx;
                    restoreCmd.CommandTimeout = 300;
                    restoreCmd.CommandText = @"
                        INSERT INTO DonationData 
                        (Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, 
                         Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, 
                         DateCreated, IsAnonymous)
                        SELECT Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, 
                               Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, 
                               DateCreated, COALESCE(IsAnonymous, 0)
                        FROM DonationDataBackup 
                        WHERE BackupId = @BackupId";
                    restoreCmd.Parameters.Add(new MySqlParameter("@BackupId", latestBackupId));
                    
                    var restoredRows = await restoreCmd.ExecuteNonQueryAsync(cancellationToken);

                    // Pin this backup to prevent it from being deleted
                    var pinCmd = conn.CreateCommand();
                    pinCmd.Transaction = (MySqlTransaction)tx;
                    pinCmd.CommandText = @"
                        UPDATE DonationDataBackup 
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

        private async Task<RollbackResult> RollbackAccountingAsync(CancellationToken cancellationToken)
        {
            var result = new RollbackResult();
            var connStr = _config.GetConnectionString("default") ?? string.Empty;

            try
            {
                // Build connection string with extended timeouts for rollback operations
                var csb = new MySqlConnectionStringBuilder(connStr)
                {
                    ConnectionTimeout = 60, // 1 minute for connection
                    DefaultCommandTimeout = 300 // 5 minutes for commands
                };

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

                    // Find the most recent non-pinned backup
                    var getLatestBackupCmd = conn.CreateCommand();
                    getLatestBackupCmd.Transaction = (MySqlTransaction)tx;
                    getLatestBackupCmd.CommandText = @"
                        SELECT BackupId, BackupAt, COUNT(*) as RecordCount
                        FROM AccountingDataBackup 
                        WHERE Pinned = 0
                        GROUP BY BackupId, BackupAt
                        ORDER BY BackupAt DESC
                        LIMIT 1";

                    string? latestBackupId = null;
                    DateTime? backupDate = null;
                    int backupRecordCount = 0;

                    using var reader = await getLatestBackupCmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync())
                    {
                        latestBackupId = reader.GetGuid(0).ToString();  // BackupId - convert GUID to string
                        backupDate = reader.GetDateTime(1);    // BackupAt  
                        backupRecordCount = reader.GetInt32(2); // RecordCount
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
                        UPDATE AccountingDataBackup 
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
                    ? new BackupInfo { BackupId = donationBackups[0].BackupId, BackupAt = donationBackups[0].BackupAt, RecordCount = donationBackups[0].RecordCount }
                    : null;

                var accountingBackups = await _rollbackRepository.GetAvailableAccountingBackupsAsync();
                info.AccountingBackupsAvailable = accountingBackups.Any();
                info.LatestAccountingBackup = accountingBackups.Any()
                    ? new BackupInfo { BackupId = accountingBackups[0].BackupId, BackupAt = accountingBackups[0].BackupAt, RecordCount = accountingBackups[0].RecordCount }
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