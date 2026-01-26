using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace cya2.Services.Imports
{
    public class RollbackService
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<RollbackService> _logger;
        private readonly AppState _appState;

        public RollbackService(IDataAccess data, IConfiguration config, ILogger<RollbackService> logger, AppState appState)
        {
            _data = data;
            _config = config;
            _logger = logger;
            _appState = appState;
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
                        if (result.Success)
                        {
                            _appState.ClearDonationDataCache();
                            _logger.LogInformation("Cleared current admin user's donation data cache after successful rollback");
                        }
                        break;
                    case "accounting":
                        result = await RollbackAccountingAsync(cancellationToken);
                        if (result.Success)
                        {
                            _appState.ClearAccountingDataCache();
                            _logger.LogInformation("Cleared current admin user's accounting data cache after successful rollback");
                        }
                        break;
                    case "both":
                        var donationResult = await RollbackDonationsAsync(cancellationToken);
                        var accountingResult = await RollbackAccountingAsync(cancellationToken);
                        
                        result.Success = donationResult.Success && accountingResult.Success;
                        result.Message = $"Donations: {donationResult.Message}, Accounting: {accountingResult.Message}";
                        result.DonationRowsRestored = donationResult.DonationRowsRestored;
                        result.AccountingRowsRestored = accountingResult.AccountingRowsRestored;
                        
                        if (!donationResult.Success || !accountingResult.Success)
                        {
                            result.ErrorMessage = $"Errors: {donationResult.ErrorMessage} {accountingResult.ErrorMessage}".Trim();
                        }
                        
                        // Clear caches if any rollback was successful - only for current admin user
                        if (donationResult.Success || accountingResult.Success)
                        {
                            _appState.ClearAllDataCaches();
                            _logger.LogInformation("Cleared current admin user's data caches after successful rollback operation");
                        }
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
            var connStr = _config.GetConnectionString("default") ?? string.Empty;

            try
            {
                // Check donations backup availability
                var donationBackups = await _data.LoadData<BackupInfo, dynamic>(
                    @"SELECT CAST(BackupId AS CHAR(36)) AS BackupId, BackupAt, COUNT(*) as RecordCount
                      FROM DonationDataBackup 
                      WHERE Pinned = 0
                      GROUP BY BackupId, BackupAt
                      ORDER BY BackupAt DESC
                      LIMIT 5",
                    new { }, connStr);

                info.DonationBackupsAvailable = donationBackups?.Any() == true;
                info.LatestDonationBackup = donationBackups?.FirstOrDefault();

                // Check accounting backup availability
                var accountingBackups = await _data.LoadData<BackupInfo, dynamic>(
                    @"SELECT CAST(BackupId AS CHAR(36)) AS BackupId, BackupAt, COUNT(*) as RecordCount
                      FROM AccountingDataBackup 
                      WHERE Pinned = 0
                      GROUP BY BackupId, BackupAt
                      ORDER BY BackupAt DESC
                      LIMIT 5",
                    new { }, connStr);

                info.AccountingBackupsAvailable = accountingBackups?.Any() == true;
                info.LatestAccountingBackup = accountingBackups?.FirstOrDefault();

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

    public class RollbackResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int DonationRowsRestored { get; set; }
        public int AccountingRowsRestored { get; set; }
    }

    public class RollbackAvailabilityInfo
    {
        public bool CanRollback { get; set; }
        public bool DonationBackupsAvailable { get; set; }
        public bool AccountingBackupsAvailable { get; set; }
        public BackupInfo? LatestDonationBackup { get; set; }
        public BackupInfo? LatestAccountingBackup { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class BackupInfo
    {
        public string BackupId { get; set; } = string.Empty;
        public DateTime BackupAt { get; set; }
        public int RecordCount { get; set; }
    }
}