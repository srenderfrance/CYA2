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
    internal sealed class DonationImportService : IDonationImportService
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Data, string FileName, string ContentType, DateTime CreatedAt)> _previews
            = new();

        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<DonationImportService> _logger;
        private readonly ImportProgressService _progressService;
        private readonly AppState _appState;

        public DonationImportService(IDataAccess data, IConfiguration config, ILogger<DonationImportService> logger, ImportProgressService progressService, AppState appState)
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
            _progressService.Start(progressId, "Donations");
            return await ProcessAsync(file, ct, progressId);
        }

        public async Task<ImportResult> StartImportAsync(Stream file, CancellationToken ct)
        {
            var result = new ImportResult();
            var progressId = Guid.NewGuid().ToString("N");
            _progressService.Start(progressId, "Donations");
            result.ProgressId = progressId;

            // Buffer the uploaded stream now (request stream will be disposed after response)
            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                data = ms.ToArray();
            }

            // process in background while UI polls
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
                    // keep dialog open
                }
            });

            return result;
        }

        public async Task<FilePreviewResult> PreviewAsync(Stream file, string fileName, string contentType, CancellationToken ct)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            // Buffer the stream to a byte[] once so we can reuse it on confirm
            byte[] data;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                data = ms.ToArray();
            }

            var previewId = Guid.NewGuid().ToString("N");

            _previews[previewId] = (data, fileName, contentType, DateTime.UtcNow);

            _logger.LogInformation("Created donation file preview {PreviewId} for {FileName} ({Size} bytes, {ContentType})",
                previewId, fileName, data.LongLength, contentType ?? string.Empty);

            // Minimal metadata: name, size, format
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
                _logger.LogWarning("Donation preview {PreviewId} not found or expired", previewId);
                var res = new ImportResult();
                res.Errors.Add("Preview session expired. Please upload the file again.");
                return res;
            }

            // Recreate the stream and call the existing ImportAsync pipeline
            using var ms = new MemoryStream(entry.Data, writable: false);
            return await ImportAsync(ms, ct);
        }

        public async Task<ImportResult> StartImportFromPreviewAsync(string previewId, string progressId)
        {
            var result = new ImportResult();
            var pId = string.IsNullOrWhiteSpace(progressId) ? Guid.NewGuid().ToString("N") : progressId;
            _progressService.Start(pId, "Donations");
            result.ProgressId = pId;

            if (string.IsNullOrWhiteSpace(previewId))
            {
                result.Errors.Add("PreviewId is required");
                _progressService.SetStatus(pId, "PreviewId is required");
                return result;
            }

            if (!_previews.TryRemove(previewId, out var entry))
            {
                _logger.LogWarning("Donation preview {PreviewId} not found or expired", previewId);
                result.Errors.Add("Preview session expired. Please upload the file again.");
                _progressService.SetStatus(pId, "Preview session expired");
                return result;
            }

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
                    _logger.LogError(ex, "Background donation import failed for preview {PreviewId}", previewId);
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
                    _progressService.SetStatus(progressId, "Backing up existing donation data...");
                    var rows = await _data.LoadData<DonationsDataModel, dynamic>(
                        "SELECT Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous FROM DonationData",
                        new { }, connStr);

                    var dir = Path.Combine(AppContext.BaseDirectory, "App_Data", "backups");
                    Directory.CreateDirectory(dir);
                    var fileName = Path.Combine(dir, $"DonationData_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");

                    using var sw = new StreamWriter(fileName, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    // header
                    await sw.WriteLineAsync("Id,Date,AccountName,PaymentMethod,GiftType,Amount,Fund,SoftCreditName,Address,City,State,PostalCode,Country,Email,PhoneFixed,PhoneMobile,DateCreated,IsAnonymous");
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
                                Q(dateStr),
                                Q(r.AccountName),
                                Q(r.PaymentMethod),
                                Q(r.GiftType),
                                r.Amount.ToString(CultureInfo.InvariantCulture),
                                Q(r.Fund),
                                Q(r.SoftCreditName ?? string.Empty),
                                Q(r.Address ?? string.Empty),
                                Q(r.City ?? string.Empty),
                                Q(r.State ?? string.Empty),
                                Q(r.PostalCode ?? string.Empty),
                                Q(r.Country ?? string.Empty),
                                Q(r.Email ?? string.Empty),
                                Q(r.PhoneFixed ?? string.Empty),
                                Q(r.PhoneMobile ?? string.Empty),
                                Q(createdStr),
                                r.IsAnonymous ? "1" : "0"
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Donation backup failed");
                    _progressService.SetStatus(progressId, $"Backup failed: {ex.Message}");
                }
            }

            if (doDelete)
            {
                try
                {
                    _progressService.SetStatus(progressId, "Clearing existing donation data...");
                    await _data.SaveData("DELETE FROM DonationData", new { }, connStr);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Donation delete failed");
                    _progressService.SetStatus(progressId, $"Delete failed: {ex.Message}");
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

                await EnsureDonationBackupTableAsync(conn, (MySqlTransaction)tx, ct);

                string backupId = Guid.NewGuid().ToString();

                // Check how many rows we'll be backing up
                _progressService.UpdateStep(progressId, "Database Backup", "Counting existing records...");
                var countCmd = conn.CreateCommand();
                countCmd.Transaction = (MySqlTransaction)tx;
                countCmd.CommandTimeout = 30;
                countCmd.CommandText = "SELECT COUNT(*) FROM DonationData WHERE Date >= @from";
                countCmd.Parameters.Add(new MySqlParameter("@from", fromDate));
                var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                
                if (rowCount > 0)
                {
                    _progressService.UpdateStep(progressId, "Database Backup", $"Backing up {rowCount:N0} records...");
                    
                    // Detect optional IsAnonymous column in live table
                    bool hasIsAnon = await ColumnExistsAsync(conn, (MySqlTransaction)tx, "DonationData", "IsAnonymous", ct);

                    // Build INSERT ... SELECT with explicit columns and timeout
                    var insert = conn.CreateCommand();
                    insert.Transaction = (MySqlTransaction)tx;
                    insert.CommandTimeout = 300; // 5 minutes for large backup operations
                    
                    if (hasIsAnon)
                    {
                        insert.CommandText = @"INSERT INTO DonationDataBackup
                            (Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous, BackupId, BackupAt, Pinned, SourceRangeStart)
                            SELECT Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous, @bid, UTC_TIMESTAMP(), 0, @from
                            FROM DonationData WHERE Date >= @from";
                    }
                    else
                    {
                        insert.CommandText = @"INSERT INTO DonationDataBackup
                            (Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, BackupId, BackupAt, Pinned, SourceRangeStart)
                            SELECT Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, @bid, UTC_TIMESTAMP(), 0, @from
                            FROM DonationData WHERE Date >= @from";
                    }
                    insert.Parameters.Add(new MySqlParameter("@bid", backupId));
                    insert.Parameters.Add(new MySqlParameter("@from", fromDate));
                    
                    var backupRows = await insert.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", $"Removing {rowCount:N0} old records...");

                    // Delete from live with timeout
                    var del = conn.CreateCommand();
                    del.Transaction = (MySqlTransaction)tx;
                    del.CommandTimeout = 300; // 5 minutes for large delete operations
                    del.CommandText = "DELETE FROM DonationData WHERE Date >= @from";
                    del.Parameters.Add(new MySqlParameter("@from", fromDate));
                    var deletedRows = await del.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", "Cleaning up old backups...");

                    // Retention cleanup (non-pinned)
                    await CleanupDonationBackupsAsync(conn, (MySqlTransaction)tx, ct);
                }
                else
                {
                    _progressService.UpdateStep(progressId, "Database Backup", "No existing records found for date range");
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup/delete range for donations failed");
                _progressService.UpdateStep(progressId, "Database Backup", $"Failed: {ex.Message}");
                throw; // Re-throw to let the import process handle the error appropriately
            }
        }

        private async Task<ImportResult> ProcessAsync(Stream file, CancellationToken ct, string progressId)
        {
            var result = new ImportResult { ProgressId = progressId };

            using var package = new ExcelPackage(file);
            var ws = package.Workbook.Worksheets[0];
            if (ws == null)
            {
                result.Errors.Add("No worksheet found");
                _logger.LogWarning("Donation import: no worksheet found");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.SetStatus(progressId, "No worksheet found");
                return result;
            }

            // Step 1: File Validation
            _progressService.AddStep(progressId, "File Validation", "Validating Excel file structure...");
            
            // Donations header is at row 1
            int headerRow = 1;
            int firstDataRow = headerRow + 1;

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.Dimension?.End.Column ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var header = ws.Cells[headerRow, c]?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(header) && !map.ContainsKey(header))
                    map[header] = c;
            }

            string[] required = new[] {
                "Gift Date","Name","Gift Payment Type","Gift Type","Fund Split Amount","Fund Notes",
                "Soft Credit Recipient Name","Preferred Address Line 1","Preferred City","Preferred State",
                "Preferred ZIP","Preferred Country","Personal Email Number","Home Phone Number",
                "Personal Mobile Phone Number","Gift Is Anonymous"
            };
            foreach (var col in required)
            {
                if (!map.ContainsKey(col)) result.Errors.Add($"Missing column: {col}");
            }
            if (result.Errors.Count > 0)
            {
                _logger.LogWarning("Donation import: missing required columns: {Errors}", string.Join(",", result.Errors));
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
            for (int r = firstDataRow; r <= (ws.Dimension?.End.Row ?? 0); r++)
            {
                var dateTxt0 = ws.Cells[r, map["Gift Date"]]?.Text?.Trim();
                if (ExcelParsingHelpers.TryParseDateUS(dateTxt0, out var d))
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
            _progressService.AddStep(progressId, "Data Import", "Importing donation records...");
            _progressService.SetExpected(progressId, totalDataRows);
            _progressService.Report(progressId, 0, 0, 0, "Starting import of new data...");
            
            // Get lastRow from worksheet dimensions
            int lastRow = ws.Dimension?.End.Row ?? 0;
            var batch = new List<DonationRowDto>(capacity: 1024);
            int batchSize = _config.GetValue<int>("Import:BatchSize", 1000);
            _logger.LogInformation("Donation import starting. Worksheet rows: {LastRow}, data begins at {FirstDataRow}", lastRow, firstDataRow);
            
            for (int r = firstDataRow; r <= lastRow; r++)
            {
                if (ct.IsCancellationRequested) break;

                var dateTxt = ws.Cells[r, map["Gift Date"]]?.Text?.Trim();
                var name = ws.Cells[r, map["Name"]]?.Text?.Trim();
                var payType = ws.Cells[r, map["Gift Payment Type"]]?.Text?.Trim();
                var giftType = ws.Cells[r, map["Gift Type"]]?.Text?.Trim();
                var amountTxt = ws.Cells[r, map["Fund Split Amount"]]?.Text?.Trim();
                var fund = ws.Cells[r, map["Fund Notes"]]?.Text?.Trim();
                var soft = ws.Cells[r, map["Soft Credit Recipient Name"]]?.Text?.Trim();
                var addr = ws.Cells[r, map["Preferred Address Line 1"]]?.Text?.Trim();
                var city = ws.Cells[r, map["Preferred City"]]?.Text?.Trim();
                var state = ws.Cells[r, map["Preferred State"]]?.Text?.Trim();
                var zip = ws.Cells[r, map["Preferred ZIP"]]?.Text?.Trim();
                var country = ws.Cells[r, map["Preferred Country"]]?.Text?.Trim();
                var email = ws.Cells[r, map["Personal Email Number"]]?.Text?.Trim();
                var phone = ws.Cells[r, map["Home Phone Number"]]?.Text?.Trim();
                var mobile = ws.Cells[r, map["Personal Mobile Phone Number"]]?.Text?.Trim();
                var anonTxt = ws.Cells[r, map["Gift Is Anonymous"]]?.Text?.Trim();

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fund) && string.IsNullOrWhiteSpace(amountTxt)) continue;

                if (!ExcelParsingHelpers.TryParseDateUS(dateTxt, out var date))
                {
                    result.FailedRows++; result.Errors.Add($"Row {r}: invalid Gift Date '{dateTxt}'");
                    continue;
                }
                if (!ExcelParsingHelpers.TryParseDoubleUS(amountTxt, out var amount))
                {
                    result.FailedRows++; result.Errors.Add($"Row {r}: invalid Amount '{amountTxt}'");
                    continue;
                }

                batch.Add(new DonationRowDto
                {
                    Date = date,
                    AccountName = name ?? string.Empty,
                    PaymentMethod = payType ?? string.Empty,
                    GiftType = giftType ?? string.Empty,
                    Amount = amount,
                    Fund = fund ?? string.Empty,
                    SoftCreditName = soft ?? string.Empty,
                    Address = addr ?? string.Empty,
                    City = city ?? string.Empty,
                    State = state ?? string.Empty,
                    PostalCode = zip ?? string.Empty,
                    Country = country ?? string.Empty,
                    Email = email ?? string.Empty,
                    PhoneFixed = phone ?? string.Empty,
                    PhoneMobile = mobile ?? string.Empty,
                    IsAnonymous = ExcelParsingHelpers.ParseYesNo(anonTxt),
                    DateCreated = DateTime.UtcNow
                });

                result.TotalRows++;

                if (batch.Count >= batchSize)
                {
                    var inserted = await BulkInsertAsync(batch);
                    result.InsertedRows += inserted;
                    _logger.LogInformation("Inserted batch of {BatchCount} donation rows, inserted {Inserted}", batch.Count, inserted);
                    batch.Clear();
                    _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows, "Importing data...");
                }
            }

            if (batch.Count > 0)
            {
                var inserted = await BulkInsertAsync(batch);
                result.InsertedRows += inserted;
                _logger.LogInformation("Inserted final batch of {BatchCount} donation rows, inserted {Inserted}", batch.Count, inserted);
                batch.Clear();
                _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows, "Finalizing...");
            }

            _progressService.CompleteStep(progressId, "Data Import", "Completed", $"{result.InsertedRows:N0} records imported, {result.FailedRows:N0} failed");

            _logger.LogInformation("Donation import complete. TotalRows={TotalRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}", result.TotalRows, result.InsertedRows, result.FailedRows);
            _progressService.AddErrors(progressId, result.Errors);
            _progressService.Complete(progressId);
            
            // Clear only the current admin user's donation data cache after successful import
            if (result.InsertedRows > 0)
            {
                _appState.ClearDonationDataCache();
                _logger.LogInformation("Cleared current admin user's donation data cache after successful import");
            }
            
            return result;
        }

        private async Task<int> BulkInsertAsync(List<DonationRowDto> batch)
        {
            if (batch == null || batch.Count == 0) return 0;

            var connStr = _config.GetConnectionString("default") ?? string.Empty;
            string tempFile = Path.Combine(Path.GetTempPath(), $"donation_import_{Guid.NewGuid():N}.csv");
            try
            {
                var sb = new StringBuilder();
                foreach (var r in batch)
                {
                    string Quote(string s)
                    {
                        if (s is null) return "";
                        return '"' + s.Replace("\"", "\"\"") + '"';
                    }

                    var dateStr = r.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    var amountStr = r.Amount.ToString(CultureInfo.InvariantCulture);

                    sb.Append(Quote(dateStr)); sb.Append(',');
                    sb.Append(Quote(r.AccountName)); sb.Append(',');
                    sb.Append(Quote(r.PaymentMethod)); sb.Append(',');
                    sb.Append(Quote(r.GiftType)); sb.Append(',');
                    sb.Append(Quote(amountStr)); sb.Append(',');
                    sb.Append(Quote(r.Fund)); sb.Append(',');
                    sb.Append(Quote(r.SoftCreditName)); sb.Append(',');
                    sb.Append(Quote(r.Address)); sb.Append(',');
                    sb.Append(Quote(r.City)); sb.Append(',');
                    sb.Append(Quote(r.State)); sb.Append(',');
                    sb.Append(Quote(r.PostalCode)); sb.Append(',');
                    sb.Append(Quote(r.Country)); sb.Append(',');
                    sb.Append(Quote(r.Email)); sb.Append(',');
                    sb.Append(Quote(r.PhoneFixed)); sb.Append(',');
                    sb.Append(Quote(r.PhoneMobile)); sb.Append(',');
                    sb.Append(Quote(r.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                    sb.Append('\n');
                }
                await File.WriteAllTextAsync(tempFile, sb.ToString(), Encoding.UTF8);

                var csb = new MySqlConnectionStringBuilder(connStr);
                // New: respect config flag to disable local infile
                bool configAllowsLocalInfile = _config.GetValue<bool>("Import:UseLocalInfile", false);
                bool allowLocalInfile = configAllowsLocalInfile && csb.AllowLoadLocalInfile;
                if (!allowLocalInfile)
                {
                    _logger.LogWarning("LOCAL INFILE disabled or not available. Skipping MySqlBulkLoader for donations.");
                }

                using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync();

                int maxAttempts = _config.GetValue<int>("Import:MaxAttempts", 3);
                Exception? lastLoaderEx = null;

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
                        Local = true,
                    };
                    loader.Columns.Add("Date");
                    loader.Columns.Add("AccountName");
                    loader.Columns.Add("PaymentMethod");
                    loader.Columns.Add("GiftType");
                    loader.Columns.Add("Amount");
                    loader.Columns.Add("Fund");
                    loader.Columns.Add("SoftCreditName");
                    loader.Columns.Add("Address");
                    loader.Columns.Add("City");
                    loader.Columns.Add("State");
                    loader.Columns.Add("PostalCode");
                    loader.Columns.Add("Country");
                    loader.Columns.Add("Email");
                    loader.Columns.Add("PhoneFixed");
                    loader.Columns.Add("PhoneMobile");
                    loader.Columns.Add("DateCreated");

                    int attempt = 0;
                    while (attempt < maxAttempts)
                    {
                        attempt++;
                        try
                        {
                            var loaded = await Task.Run(() => (int)loader.Load());
                            return loaded;
                        }
                        catch (Exception ex)
                        {
                            lastLoaderEx = ex;
                            _logger.LogWarning(ex, "MySqlBulkLoader for donations attempt {Attempt} failed", attempt);
                            if (attempt < maxAttempts) await Task.Delay(250 * attempt);
                        }
                    }
                }

                // Fallback: multi-row INSERT
                try
                {
                    int insertAttempt = 0;
                    while (insertAttempt < maxAttempts)
                    {
                        insertAttempt++;
                        try
                        {
                            using var tx = await conn.BeginTransactionAsync();
                            var cols = new[] { "Date", "AccountName", "PaymentMethod", "GiftType", "Amount", "Fund", "SoftCreditName", "Address", "City", "State", "PostalCode", "Country", "Email", "PhoneFixed", "PhoneMobile", "DateCreated" };
                            var values = new List<string>(batch.Count);
                            var cmd = conn.CreateCommand();
                            cmd.Transaction = tx;
                            int paramIndex = 0;
                            foreach (var row in batch)
                            {
                                var pNames = new List<string>();
                                for (int c = 0; c < cols.Length; c++) pNames.Add("@p" + (paramIndex++));
                                values.Add("(" + string.Join(',', pNames) + ")");

                                cmd.Parameters.Add(new MySqlParameter(pNames[0], row.Date));
                                cmd.Parameters.Add(new MySqlParameter(pNames[1], row.AccountName));
                                cmd.Parameters.Add(new MySqlParameter(pNames[2], row.PaymentMethod));
                                cmd.Parameters.Add(new MySqlParameter(pNames[3], row.GiftType));
                                cmd.Parameters.Add(new MySqlParameter(pNames[4], row.Amount));
                                cmd.Parameters.Add(new MySqlParameter(pNames[5], row.Fund));
                                cmd.Parameters.Add(new MySqlParameter(pNames[6], row.SoftCreditName));
                                cmd.Parameters.Add(new MySqlParameter(pNames[7], row.Address));
                                cmd.Parameters.Add(new MySqlParameter(pNames[8], row.City));
                                cmd.Parameters.Add(new MySqlParameter(pNames[9], row.State));
                                cmd.Parameters.Add(new MySqlParameter(pNames[10], row.PostalCode));
                                cmd.Parameters.Add(new MySqlParameter(pNames[11], row.Country));
                                cmd.Parameters.Add(new MySqlParameter(pNames[12], row.Email));
                                cmd.Parameters.Add(new MySqlParameter(pNames[13], row.PhoneFixed));
                                cmd.Parameters.Add(new MySqlParameter(pNames[14], row.PhoneMobile));
                                cmd.Parameters.Add(new MySqlParameter(pNames[15], row.DateCreated));
                            }

                            cmd.CommandText = $"INSERT INTO DonationData ({string.Join(',', cols)}) VALUES {string.Join(',', values)}";
                            var affected = await cmd.ExecuteNonQueryAsync();
                            await tx.CommitAsync();
                            return affected;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Multi-row insert attempt {Attempt} failed for donations", insertAttempt);
                            if (insertAttempt < maxAttempts) await Task.Delay(250 * insertAttempt);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Multi-row insert failure for donations");
                }

                _logger.LogWarning(lastLoaderEx, "Bulk loader skipped/failed; falling back to row-by-row insert for donations.");

                // Final fallback: row-by-row
                int insertedCount = 0;
                const string sql = @"INSERT INTO DonationData
                (Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated)
                VALUES (@Date, @AccountName, @PaymentMethod, @GiftType, @Amount, @Fund, @SoftCreditName, @Address, @City, @State, @PostalCode, @Country, @Email, @PhoneFixed, @PhoneMobile, @DateCreated)";

                foreach (var row in batch)
                {
                    try
                    {
                        var res = await _data.SaveData(sql, row, connStr);
                        if (res > 0) insertedCount += res;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Row insert failed for donation row: {@Row}", row);
                    }
                }

                return insertedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Donation BulkInsertAsync");
                return 0;
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        private async Task<bool> ColumnExistsAsync(MySqlConnection conn, MySqlTransaction tx, string tableName, string columnName, CancellationToken ct)
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS 
                                 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c";
            cmd.Parameters.Add(new MySqlParameter("@t", tableName));
            cmd.Parameters.Add(new MySqlParameter("@c", columnName));
            var cnt = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return cnt > 0;
        }

        private async Task EnsureDonationBackupTableAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
        {
            // Create table with explicit schema (Id without AUTO_INCREMENT)
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
                      AND TABLE_NAME = 'DonationDataBackup' 
                      AND COLUMN_NAME = 'Id'";
                
                var extra = await checkAuto.ExecuteScalarAsync(ct) as string;
                if (!string.IsNullOrEmpty(extra) && extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("DonationDataBackup table has AUTO_INCREMENT on Id column, recreating with correct schema");
                    
                    // Need to recreate table without AUTO_INCREMENT
                    var dropTable = conn.CreateCommand();
                    dropTable.Transaction = tx;
                    dropTable.CommandText = "DROP TABLE IF EXISTS DonationDataBackup";
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

        private async Task CleanupDonationBackupsAsync(MySqlConnection conn, MySqlTransaction tx, CancellationToken ct)
        {
            // Delete older non-pinned backups for today, keep newest one
            var delToday = conn.CreateCommand();
            delToday.Transaction = tx;
            delToday.CommandTimeout = 60;
            delToday.CommandText = @"
                DELETE FROM DonationDataBackup
                WHERE Pinned = 0
                  AND DATE(BackupAt) = CURDATE()
                  AND BackupId <> (
                      SELECT keepId FROM (
                          SELECT BackupId AS keepId
                          FROM DonationDataBackup
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
                DELETE FROM DonationDataBackup
                WHERE Pinned = 0
                  AND DATE(BackupAt) < CURDATE()
                  AND BackupId <> (
                      SELECT keepId FROM (
                          SELECT BackupId AS keepId
                          FROM DonationDataBackup
                          WHERE Pinned = 0 AND DATE(BackupAt) < CURDATE()
                          GROUP BY BackupId
                          ORDER BY MAX(BackupAt) DESC
                          LIMIT 1
                      ) AS y
                  )";
            await delPrior.ExecuteNonQueryAsync(ct);
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

                await EnsureDonationBackupTableAsync(conn, (MySqlTransaction)tx, ct);

                string backupId = Guid.NewGuid().ToString();

                // Check how many rows we'll be backing up (ALL existing data)
                _progressService.UpdateStep(progressId, "Database Backup", "Counting existing records...");
                var countCmd = conn.CreateCommand();
                countCmd.Transaction = (MySqlTransaction)tx;
                countCmd.CommandTimeout = 30;
                countCmd.CommandText = "SELECT COUNT(*) FROM DonationData";
                var rowCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));
                
                if (rowCount > 0)
                {
                    _progressService.UpdateStep(progressId, "Database Backup", $"Backing up all {rowCount:N0} existing records...");
                    
                    // Detect optional IsAnonymous column in live table
                    bool hasIsAnon = await ColumnExistsAsync(conn, (MySqlTransaction)tx, "DonationData", "IsAnonymous", ct);

                    // Build INSERT ... SELECT to backup ALL existing data
                    var insert = conn.CreateCommand();
                    insert.Transaction = (MySqlTransaction)tx;
                    insert.CommandTimeout = 300; // 5 minutes for large backup operations
                    
                    if (hasIsAnon)
                    {
                        insert.CommandText = @"INSERT INTO DonationDataBackup
                            (Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous, BackupId, BackupAt, Pinned, SourceRangeStart)
                            SELECT Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous, @bid, UTC_TIMESTAMP(), 0, @from
                            FROM DonationData";
                    }
                    else
                    {
                        insert.CommandText = @"INSERT INTO DonationDataBackup
                            (Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, BackupId, BackupAt, Pinned, SourceRangeStart)
                            SELECT Id, Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, @bid, UTC_TIMESTAMP(), 0, @from
                            FROM DonationData";
                    }
                    insert.Parameters.Add(new MySqlParameter("@bid", backupId));
                    insert.Parameters.Add(new MySqlParameter("@from", fromDate));
                    
                    var backupRows = await insert.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", $"Removing records from {fromDate:yyyy-MM-dd} forward...");

                    // Delete only from the import start date forward
                    var del = conn.CreateCommand();
                    del.Transaction = (MySqlTransaction)tx;
                    del.CommandTimeout = 300; // 5 minutes for large delete operations
                    del.CommandText = "DELETE FROM DonationData WHERE Date >= @from";
                    del.Parameters.Add(new MySqlParameter("@from", fromDate));
                    var deletedRows = await del.ExecuteNonQueryAsync(ct);
                    
                    _progressService.UpdateStep(progressId, "Database Backup", "Cleaning up old backups...");

                    // Retention cleanup (non-pinned)
                    await CleanupDonationBackupsAsync(conn, (MySqlTransaction)tx, ct);
                }
                else
                {
                    _progressService.UpdateStep(progressId, "Database Backup", "No existing records found");
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup all data and delete range for donations failed");
                _progressService.UpdateStep(progressId, "Database Backup", $"Failed: {ex.Message}");
                throw; // Re-throw to let the import process handle the error appropriately
            }
        }
    }
}
