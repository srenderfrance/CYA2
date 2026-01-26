using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using System.Text;
using System.Globalization;
using MySql.Data.MySqlClient;
using ModelsLibrary;

namespace cya2.Services.Imports
{
    internal sealed class AccountingImportService : IAccountingImportService
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Data, string FileName, string ContentType, DateTime CreatedAt)> _previews
            = new();

        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<AccountingImportService> _logger;
        private readonly ImportProgressService _progressService;
        private readonly AppState _appState;

        public AccountingImportService(IDataAccess data, IConfiguration config, ILogger<AccountingImportService> logger, ImportProgressService progressService, AppState appState)
        {
            _data = data;
            _config = config;
            _logger = logger;
            _progressService = progressService;
            _appState = appState;
        }

        public async Task<ImportResult> ImportAsync(Stream file, CancellationToken ct)
        {
            var progressId = Guid.NewGuid().ToString("N");
            _progressService.Start(progressId, "Accounting");
            return await ProcessAsync(file, ct, progressId);
        }

        public async Task<ImportResult> StartImportAsync(Stream file, CancellationToken ct)
        {
            var result = new ImportResult();
            var progressId = Guid.NewGuid().ToString("N");
            _progressService.Start(progressId, "Accounting");
            result.ProgressId = progressId;

            // Buffer request stream immediately
            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                data = ms.ToArray();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    using var ms = new MemoryStream(data, writable: false);
                    await ProcessAsync(ms, CancellationToken.None, progressId);
                }
                catch (Exception ex)
                {
                    _progressService.SetStatus(progressId, $"Error: {ex.Message}");
                }
            });

            return result;
        }

        public async Task<FilePreviewResult> PreviewAsync(Stream file, string fileName, string contentType, CancellationToken ct)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                data = ms.ToArray();
            }

            var previewId = Guid.NewGuid().ToString("N");

            _previews[previewId] = (data, fileName, contentType, DateTime.UtcNow);

            _logger.LogInformation("Created accounting file preview {PreviewId} for {FileName} ({Size} bytes, {ContentType})",
                previewId, fileName, data.LongLength, contentType ?? string.Empty);

            return new FilePreviewResult
            {
                PreviewId = previewId,
                FileName = fileName ?? string.Empty,
                FileSizeBytes = data.LongLength,
                ContentType = contentType ?? string.Empty
            };
        }

        public async Task<ImportResult> ImportFromPreviewAsync(string previewId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(previewId))
                throw new ArgumentException("PreviewId is required", nameof(previewId));

            if (!_previews.TryRemove(previewId, out var entry))
            {
                _logger.LogWarning("Accounting preview {PreviewId} not found or expired", previewId);
                var res = new ImportResult();
                res.Errors.Add("Preview session expired. Please upload the file again.");
                return res;
            }

            using var ms = new MemoryStream(entry.Data, writable: false);
            return await ImportAsync(ms, ct);
        }

        public async Task<ImportResult> StartImportFromPreviewAsync(string previewId, string progressId)
        {
            var result = new ImportResult();
            var pId = string.IsNullOrWhiteSpace(progressId) ? Guid.NewGuid().ToString("N") : progressId;
            _progressService.Start(pId, "Accounting");
            result.ProgressId = pId;

            if (string.IsNullOrWhiteSpace(previewId))
            {
                result.Errors.Add("PreviewId is required");
                _progressService.SetStatus(pId, "PreviewId is required");
                return result;
            }

            if (!_previews.TryRemove(previewId, out var entry))
            {
                _logger.LogWarning("Accounting preview {PreviewId} not found or expired", previewId);
                result.Errors.Add("Preview session expired. Please upload the file again.");
                _progressService.SetStatus(pId, "Preview session expired");
                return result;
            }

            // Buffer data for background processing
            var data = entry.Data;

            _ = Task.Run(async () =>
            {
                try
                {
                    using var ms = new MemoryStream(data, writable: false);
                    await ProcessAsync(ms, CancellationToken.None, pId);
                }
                catch (Exception ex)
                {
                    _progressService.SetStatus(pId, $"Error: {ex.Message}");
                    _logger.LogError(ex, "Background accounting import failed for preview {PreviewId}", previewId);
                }
            });

            return result;
        }

        private async Task BackupAndMaybeDeleteAsync(string progressId, CancellationToken ct)
        {
            var connStr = _config.GetConnectionString("default") ?? string.Empty;
            bool doBackup = _config.GetValue<bool>("Import:BackupBeforeImport", false);
            bool doDelete = _config.GetValue<bool>("Import:DeleteBeforeImport", false);

            if (doBackup)
            {
                try
                {
                    _progressService.UpdateStep(progressId, "Legacy Backup", "Creating CSV backup...");
                    var rows = await _data.LoadData<AccountingDataModel, dynamic>(
                        "SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated FROM AccountingData",
                        new { }, connStr);

                    var dir = Path.Combine(AppContext.BaseDirectory, "App_Data", "backups");
                    Directory.CreateDirectory(dir);
                    var fileName = Path.Combine(dir, $"AccountingData_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

                    using var sw = new StreamWriter(fileName, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    await sw.WriteLineAsync("Id,AccountingClass,Date,Num,Amount,AccountNumber,Account,Type,DateCreated");
                    if (rows != null)
                    {
                        foreach (var r in rows)
                        {
                            string Q(string? s) => s is null ? string.Empty : '"' + s.Replace("\"", "\"\"") + '"';
                            string dateStr = r.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                            string createdStr = r.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                            await sw.WriteLineAsync(string.Join(',', new[]
                            {
                                r.Id.ToString(CultureInfo.InvariantCulture),
                                Q(r.AccountingClass),
                                Q(dateStr),
                                Q(r.Num),
                                r.Amount.ToString(CultureInfo.InvariantCulture),
                                Q(r.AccountNumber),
                                Q(r.Account),
                                Q(r.Type),
                                Q(createdStr)
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Accounting backup failed");
                    _progressService.UpdateStep(progressId, "Legacy Backup", $"Backup failed: {ex.Message}");
                }
            }

            if (doDelete)
            {
                try
                {
                    _progressService.UpdateStep(progressId, "Legacy Backup", "Clearing existing accounting data...");
                    await _data.SaveData("DELETE FROM AccountingData", new { }, connStr);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Accounting delete failed");
                    _progressService.UpdateStep(progressId, "Legacy Backup", $"Delete failed: {ex.Message}");
                }
            }
        }

        private async Task BackupRangeToTableAndDeleteAsync(DateTime fromDate, string progressId, CancellationToken ct)
        {
            try
            {
                _progressService.UpdateStep(progressId, "Database Backup", "Connecting to database...");
                var connStr = _config.GetConnectionString("default") ?? string.Empty;
                
                // Build connection string with extended timeouts for backup operations
                var csb = new MySqlConnectionStringBuilder(connStr)
                {
                    ConnectionTimeout = 60, // 1 minute for connection
                    DefaultCommandTimeout = 300 // 5 minutes for commands
                };

                await using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                await EnsureAccountingBackupTableAsync(conn, (MySqlTransaction)tx, ct);

                string backupId = Guid.NewGuid().ToString();

                // Check how many rows we'll be backing up
                _progressService.UpdateStep(progressId, "Database Backup", "Counting existing records...");
                var countCmd = conn.CreateCommand();
                countCmd.Transaction = (MySqlTransaction)tx;
                countCmd.CommandTimeout = 30;
                countCmd.CommandText = "SELECT COUNT(*) FROM AccountingData WHERE Date >= @from";
                countCmd.Parameters.Add(new MySqlParameter("@from", fromDate));
                var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                
                if (rowCount > 0)
                {
                    _progressService.UpdateStep(progressId, "Database Backup", $"Backing up {rowCount:N0} records...");

                    // Build INSERT ... SELECT with explicit columns and timeout
                    var insert = conn.CreateCommand();
                    insert.Transaction = (MySqlTransaction)tx;
                    insert.CommandTimeout = 300; // 5 minutes for large backup operations
                    insert.CommandText = @"INSERT INTO AccountingDataBackup
                        (Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated, BackupId, BackupAt, Pinned, SourceRangeStart)
                        SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated, @bid, UTC_TIMESTAMP(), 0, @from
                        FROM AccountingData WHERE Date >= @from";
                    insert.Parameters.Add(new MySqlParameter("@bid", backupId));
                    insert.Parameters.Add(new MySqlParameter("@from", fromDate));
                    
                    var backupRows = await insert.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", $"Removing {rowCount:N0} old records...");

                    // Delete from live with timeout
                    var del = conn.CreateCommand();
                    del.Transaction = (MySqlTransaction)tx;
                    del.CommandTimeout = 300; // 5 minutes for large delete operations
                    del.CommandText = "DELETE FROM AccountingData WHERE Date >= @from";
                    del.Parameters.Add(new MySqlParameter("@from", fromDate));
                    var deletedRows = await del.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", "Cleaning up old backups...");

                    // Retention cleanup (non-pinned)
                    await CleanupAccountingBackupsAsync(conn, (MySqlTransaction)tx, ct);
                }
                else
                {
                    _progressService.UpdateStep(progressId, "Database Backup", "No existing records found for date range");
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup/delete range for accounting failed");
                _progressService.UpdateStep(progressId, "Database Backup", $"Failed: {ex.Message}");
                throw; // Re-throw to let the import process handle the error appropriately
            }
        }

        private async Task BackupAllDataAndDeleteFromDateAsync(DateTime fromDate, string progressId, CancellationToken ct)
        {
            try
            {
                _progressService.UpdateStep(progressId, "Database Backup", "Connecting to database...");
                var connStr = _config.GetConnectionString("default") ?? string.Empty;
                
                // Build connection string with extended timeouts for backup operations
                var csb = new MySqlConnectionStringBuilder(connStr)
                {
                    ConnectionTimeout = 60, // 1 minute for connection
                    DefaultCommandTimeout = 300 // 5 minutes for commands
                };

                await using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                await EnsureAccountingBackupTableAsync(conn, (MySqlTransaction)tx, ct);

                string backupId = Guid.NewGuid().ToString();

                // Check how many rows we'll be backing up (ALL existing data)
                _progressService.UpdateStep(progressId, "Database Backup", "Counting existing records...");
                var countCmd = conn.CreateCommand();
                countCmd.Transaction = (MySqlTransaction)tx;
                countCmd.CommandTimeout = 30;
                countCmd.CommandText = "SELECT COUNT(*) FROM AccountingData";
                var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                
                if (rowCount > 0)
                {
                    _progressService.UpdateStep(progressId, "Database Backup", $"Backing up all {rowCount:N0} existing records...");

                    // Build INSERT ... SELECT to backup ALL existing data
                    var insert = conn.CreateCommand();
                    insert.Transaction = (MySqlTransaction)tx;
                    insert.CommandTimeout = 300; // 5 minutes for large backup operations
                    insert.CommandText = @"INSERT INTO AccountingDataBackup
                        (Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated, BackupId, BackupAt, Pinned, SourceRangeStart)
                        SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated, @bid, UTC_TIMESTAMP(), 0, @from
                        FROM AccountingData";
                    insert.Parameters.Add(new MySqlParameter("@bid", backupId));
                    insert.Parameters.Add(new MySqlParameter("@from", fromDate));
                    
                    var backupRows = await insert.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", $"Removing records from {fromDate:yyyy-MM-dd} forward...");

                    // Delete only from the import start date forward
                    var del = conn.CreateCommand();
                    del.Transaction = (MySqlTransaction)tx;
                    del.CommandTimeout = 300; // 5 minutes for large delete operations
                    del.CommandText = "DELETE FROM AccountingData WHERE Date >= @from";
                    del.Parameters.Add(new MySqlParameter("@from", fromDate));
                    var deletedRows = await del.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", "Cleaning up old backups...");

                    // Retention cleanup (non-pinned)
                    await CleanupAccountingBackupsAsync(conn, (MySqlTransaction)tx, ct);
                }
                else
                {
                    _progressService.UpdateStep(progressId, "Database Backup", "No existing records found");
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup all data and delete range for accounting failed");
                _progressService.UpdateStep(progressId, "Database Backup", $"Failed: {ex.Message}");
                throw; // Re-throw to let the import process handle the error appropriately
            }
        }

        private async Task EnsureAccountingBackupTableAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
        {
            // Create table with explicit schema (Id without AUTO_INCREMENT)
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

            // Check if we need to upgrade an existing table that was created with LIKE
            try
            {
                // Check if Id column has AUTO_INCREMENT
                var checkAuto = conn.CreateCommand();
                checkAuto.Transaction = tx;
                checkAuto.CommandText = @"
                    SELECT EXTRA 
                    FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = DATABASE() 
                      AND TABLE_NAME = 'AccountingDataBackup' 
                      AND COLUMN_NAME = 'Id'";
                
                var extra = await checkAuto.ExecuteScalarAsync(ct) as string;
                if (!string.IsNullOrEmpty(extra) && extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("AccountingDataBackup table has AUTO_INCREMENT on Id column, recreating with correct schema");
                    
                    // Need to recreate table without AUTO_INCREMENT
                    var dropTable = conn.CreateCommand();
                    dropTable.Transaction = tx;
                    dropTable.CommandText = "DROP TABLE IF EXISTS AccountingDataBackup";
                    await dropTable.ExecuteNonQueryAsync(ct);
                    
                    // Recreate with correct schema
                    await create.ExecuteNonQueryAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check AUTO_INCREMENT status, assuming table schema is correct");
            }
        }

        private async Task CleanupAccountingBackupsAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
        {
            // Delete older non-pinned backups for today, keep newest one
            var delToday = conn.CreateCommand();
            delToday.Transaction = tx;
            delToday.CommandTimeout = 60;
            delToday.CommandText = @"
                DELETE FROM AccountingDataBackup
                WHERE Pinned = 0
                  AND DATE(BackupAt) = CURDATE()
                  AND BackupId <> (
                      SELECT keepId FROM (
                          SELECT BackupId AS keepId
                          FROM AccountingDataBackup
                          WHERE Pinned = 0 AND DATE(BackupAt) = CURDATE()
                          GROUP BY BackupId
                          ORDER BY MAX(BackupAt) DESC
                          LIMIT 1
                      ) AS x
                  )";
            await delToday.ExecuteNonQueryAsync(ct);

            // Delete older non-pinned backups from prior days, keep newest one
            var delPrior = conn.CreateCommand();
            delPrior.Transaction = tx;
            delPrior.CommandTimeout = 60;
            delPrior.CommandText = @"
                DELETE FROM AccountingDataBackup
                WHERE Pinned = 0
                  AND DATE(BackupAt) < CURDATE()
                  AND BackupId <> (
                      SELECT keepId FROM (
                          SELECT BackupId AS keepId
                          FROM AccountingDataBackup
                          WHERE Pinned = 0 AND DATE(BackupAt) < CURDATE()
                          GROUP BY BackupId
                          ORDER BY MAX(BackupAt) DESC
                          LIMIT 1
                      ) AS y
                  )";
            await delPrior.ExecuteNonQueryAsync(ct);
        }

        private async Task<ImportResult> ProcessAsync(Stream file, CancellationToken ct, string progressId)
        {
            var result = new ImportResult { ProgressId = progressId };

            using var package = new ExcelPackage(file);
            var ws = package.Workbook.Worksheets[0];
            if (ws == null)
            {
                result.Errors.Add("No worksheet found");
                _logger.LogWarning("Accounting import: no worksheet found");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.SetStatus(progressId, "No worksheet found");
                return result;
            }

            // Step 1: File Validation
            _progressService.AddStep(progressId, "File Validation", "Validating Excel file structure...");

            int headerRow = 5;
            int firstDataRow = headerRow + 1;

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.Dimension?.End.Column ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var header = ws.Cells[headerRow, c]?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(header) && !map.ContainsKey(header))
                    map[header] = c;
            }

            string[] required = new[] { "Class", "Date", "Num", "Amount", "Account #", "Account", "Type" };
            foreach (var col in required)
            {
                if (!map.ContainsKey(col)) result.Errors.Add($"Missing column: {col}");
            }
            if (result.Errors.Count > 0)
            {
                _logger.LogWarning("Accounting import: missing required columns: {Errors}", string.Join(",", result.Errors));
                _progressService.CompleteStep(progressId, "File Validation", "Failed - Missing required columns");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.Complete(progressId);
                return result;
            }

            int totalDataRows = Math.Max(0, (ws.Dimension?.End.Row ?? 0) - firstDataRow + 1);
            _progressService.CompleteStep(progressId, "File Validation", "Completed", $"Found {totalDataRows:N0} rows to import");

            // Step 2: Legacy CSV Backup (if enabled)
            var doBackup = _config.GetValue<bool>("Import:BackupBeforeImport", false);
            if (doBackup)
            {
                _progressService.AddStep(progressId, "Legacy Backup", "Creating CSV backup...");
                await BackupAndMaybeDeleteAsync(progressId, ct);
                _progressService.CompleteStep(progressId, "Legacy Backup", "Completed", "CSV backup created");
            }

            // Step 3: Data Analysis
            _progressService.AddStep(progressId, "Data Analysis", "Analyzing import data for date range...");
            DateTime? earliest = null;
            int lastRow = ws.Dimension?.End.Row ?? 0;
            for (int r = firstDataRow; r <= lastRow; r++)
            {
                var dt = ws.Cells[r, map["Date"]]?.Text?.Trim();
                if (ExcelParsingHelpers.TryParseDateUS(dt, out var d))
                {
                    if (earliest == null || d < earliest.Value) earliest = d;
                }
            }

            if (earliest.HasValue)
            {
                _progressService.CompleteStep(progressId, "Data Analysis", "Completed", $"Earliest date: {earliest.Value:yyyy-MM-dd}");
                
                // Step 4: Database Backup & Cleanup - Always backup ALL existing data
                _progressService.AddStep(progressId, "Database Backup", "Backing up all existing records...");
                await BackupAllDataAndDeleteFromDateAsync(earliest.Value, progressId, ct);
                _progressService.CompleteStep(progressId, "Database Backup", "Completed", "All existing data backed up, range cleared");
            }
            else
            {
                _progressService.CompleteStep(progressId, "Data Analysis", "Completed", "No valid dates found in import file");
            }

            // Step 5: Data Import
            _progressService.AddStep(progressId, "Data Import", "Importing accounting records...");
            _progressService.SetExpected(progressId, totalDataRows);
            _progressService.Report(progressId, 0, 0, 0, "Starting import of new data...");

            var batch = new List<AccountingRowDto>(capacity: 1024);
            int batchSize = _config.GetValue<int>("Import:BatchSize", 1000);
            _logger.LogInformation("Accounting import starting. Worksheet rows: {LastRow}, data begins at {FirstDataRow}", lastRow, firstDataRow);

            for (int r = firstDataRow; r <= lastRow; r++)
            {
                if (ct.IsCancellationRequested) break;

                var cls = ws.Cells[r, map["Class"]]?.Text?.Trim();
                var dtTxt = ws.Cells[r, map["Date"]]?.Text?.Trim();
                var num = ws.Cells[r, map["Num"]]?.Text?.Trim();
                var amtTxt = ws.Cells[r, map["Amount"]]?.Text?.Trim();
                var acctNum = ws.Cells[r, map["Account #"]]?.Text?.Trim();
                var acct = ws.Cells[r, map["Account"]]?.Text?.Trim();
                var type = ws.Cells[r, map["Type"]]?.Text?.Trim();

                if (string.IsNullOrWhiteSpace(cls) && string.IsNullOrWhiteSpace(num) && string.IsNullOrWhiteSpace(acct))
                    continue;

                if (!ExcelParsingHelpers.TryParseDateUS(dtTxt, out var date))
                {
                    result.FailedRows++; result.Errors.Add($"Row {r}: invalid Date '{dtTxt}'");
                    continue;
                }
                if (!ExcelParsingHelpers.TryParseDoubleUS(amtTxt, out var amount))
                {
                    result.FailedRows++; result.Errors.Add($"Row {r}: invalid Amount '{amtTxt}'");
                    continue;
                }

                batch.Add(new AccountingRowDto
                {
                    AccountingClass = cls ?? string.Empty,
                    Date = date,
                    Num = num ?? string.Empty,
                    Amount = amount,
                    AccountNumber = acctNum ?? string.Empty,
                    Account = acct ?? string.Empty,
                    Type = type ?? string.Empty,
                    DateCreated = DateTime.UtcNow
                });

                result.TotalRows++;

                if (batch.Count >= batchSize)
                {
                    var inserted = await BulkInsertAsync(batch);
                    result.InsertedRows += inserted;
                    _logger.LogInformation("Inserted batch of {BatchCount} rows, inserted {Inserted}", batch.Count, inserted);
                    batch.Clear();
                    _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows, "Importing data...");
                }
            }

            if (batch.Count > 0)
            {
                var inserted = await BulkInsertAsync(batch);
                result.InsertedRows += inserted;
                _logger.LogInformation("Inserted final batch of {BatchCount} rows, inserted {Inserted}", batch.Count, inserted);
                batch.Clear();
                _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows, "Finalizing...");
            }

            _progressService.CompleteStep(progressId, "Data Import", "Completed", $"{result.InsertedRows:N0} records imported, {result.FailedRows:N0} failed");

            _logger.LogInformation("Accounting import complete. TotalRows={TotalRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}", result.TotalRows, result.InsertedRows, result.FailedRows);
            _progressService.AddErrors(progressId, result.Errors);
            _progressService.Complete(progressId);
            
            // Clear only the current admin user's accounting data cache after successful import
            if (result.InsertedRows > 0)
            {
                _appState.ClearAccountingDataCache();
                _logger.LogInformation("Cleared current admin user's accounting data cache after successful import");
            }
            
            return result;
        }

        private async Task<int> BulkInsertAsync(List<AccountingRowDto> batch)
        {
            if (batch == null || batch.Count == 0) return 0;

            var connStr = _config.GetConnectionString("default") ?? string.Empty;
            string tempFile = Path.Combine(Path.GetTempPath(), $"acct_import_{Guid.NewGuid():N}.csv");
            try
            {
                // write CSV without header; use invariant culture
                var sb = new StringBuilder();
                foreach (var r in batch)
                {
                    static string Quote(string s)
                    {
                        if (s is null) return string.Empty;
                        return '"' + s.Replace("\"", "\"\"") + '"';
                    }

                    var dateStr = r.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    var amountStr = r.Amount.ToString(CultureInfo.InvariantCulture);
                    sb.Append(Quote(r.AccountingClass)); sb.Append(',');
                    sb.Append(Quote(dateStr)); sb.Append(',');
                    sb.Append(Quote(r.Num)); sb.Append(',');
                    sb.Append(Quote(amountStr)); sb.Append(',');
                    sb.Append(Quote(r.AccountNumber)); sb.Append(',');
                    sb.Append(Quote(r.Account)); sb.Append(',');
                    sb.Append(Quote(r.Type)); sb.Append(',');
                    sb.Append(Quote(r.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                    sb.Append('\n');
                }
                await File.WriteAllTextAsync(tempFile, sb.ToString(), Encoding.UTF8);

                var csb = new MySqlConnectionStringBuilder(connStr);
                bool configAllowsLocalInfile = _config.GetValue<bool>("Import:UseLocalInfile", false);
                bool allowLocalInfile = configAllowsLocalInfile && csb.AllowLoadLocalInfile;
                if (!allowLocalInfile)
                {
                    _logger.LogWarning("LOCAL INFILE disabled or not available. Skipping MySqlBulkLoader for accounting.");
                }

                using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync();

                int maxAttempts = _config.GetValue<int>("Import:MaxAttempts", 3);
                Exception? lastLoaderEx = null;

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
                    loader.Columns.Add("AccountingClass");
                    loader.Columns.Add("Date");
                    loader.Columns.Add("Num");
                    loader.Columns.Add("Amount");
                    loader.Columns.Add("AccountNumber");
                    loader.Columns.Add("Account");
                    loader.Columns.Add("Type");
                    loader.Columns.Add("DateCreated");

                    int attempt = 0;
                    bool loaderSucceeded = false;
                    while (attempt < maxAttempts && !loaderSucceeded)
                    {
                        attempt++;
                        try
                        {
                            var loaded = await Task.Run(() => (int)loader.Load());
                            loaderSucceeded = true;
                            return loaded;
                        }
                        catch (Exception ex)
                        {
                            lastLoaderEx = ex;
                            _logger.LogWarning(ex, "MySqlBulkLoader attempt {Attempt} failed", attempt);
                            if (attempt < maxAttempts) await Task.Delay(250 * attempt);
                        }
                    }
                }

                // Fallback: multi-row parameterized INSERT inside transaction
                try
                {
                    int insertAttempt = 0;
                    while (insertAttempt < maxAttempts)
                    {
                        insertAttempt++;
                        try
                        {
                            using var tx = await conn.BeginTransactionAsync();
                            var cols = new[] { "AccountingClass", "Date", "Num", "Amount", "AccountNumber", "Account", "Type", "DateCreated" };
                            var values = new List<string>(batch.Count);
                            var cmd = conn.CreateCommand();
                            cmd.Transaction = tx;
                            int paramIndex = 0;
                            foreach (var row in batch)
                            {
                                var pNames = new List<string>();
                                for (int c = 0; c < cols.Length; c++) pNames.Add("@p" + (paramIndex++));
                                values.Add("(" + string.Join(',', pNames) + ")");

                                // add parameters for this row
                                cmd.Parameters.Add(new MySqlParameter(pNames[0], row.AccountingClass));
                                cmd.Parameters.Add(new MySqlParameter(pNames[1], row.Date));
                                cmd.Parameters.Add(new MySqlParameter(pNames[2], row.Num));
                                cmd.Parameters.Add(new MySqlParameter(pNames[3], row.Amount));
                                cmd.Parameters.Add(new MySqlParameter(pNames[4], row.AccountNumber));
                                cmd.Parameters.Add(new MySqlParameter(pNames[5], row.Account));
                                cmd.Parameters.Add(new MySqlParameter(pNames[6], row.Type));
                                cmd.Parameters.Add(new MySqlParameter(pNames[7], row.DateCreated));
                            }

                            cmd.CommandText = $"INSERT INTO AccountingData ({string.Join(',', cols)}) VALUES {string.Join(',', values)}";
                            var affected = await cmd.ExecuteNonQueryAsync();
                            await tx.CommitAsync();
                            return affected;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Multi-row insert attempt {Attempt} failed", insertAttempt);
                            if (insertAttempt < maxAttempts) await Task.Delay(250 * insertAttempt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Multi-row insert failure");
                }

                _logger.LogWarning(lastLoaderEx, "Bulk loader skipped/failed; falling back to row-by-row insert for accounting.");

                // Final fallback: row-by-row using existing IDataAccess.SaveData
                int insertedCount = 0;
                const string sql = @"INSERT INTO AccountingData
                (AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated)
                VALUES (@AccountingClass, @Date, @Num, @Amount, @AccountNumber, @Account, @Type, @DateCreated)";

                foreach (var row in batch)
                {
                    try
                    {
                        var res = await _data.SaveData(sql, row, connStr);
                        if (res > 0) insertedCount += res;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Row insert failed for accounting row: {@Row}", row);
                    }
                }

                return insertedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during BulkInsertAsync");
                return 0;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }
    }
}
