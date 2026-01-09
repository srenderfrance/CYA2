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

namespace cya2.Services.Imports
{
    internal sealed class AccountingImportService : IAccountingImportService
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<AccountingImportService> _logger;
        private readonly ImportProgressService _progressService;

        public AccountingImportService(IDataAccess data, IConfiguration config, ILogger<AccountingImportService> logger, ImportProgressService progressService)
        {
            _data = data;
            _config = config;
            _logger = logger;
            _progressService = progressService;
        }

        public async Task<ImportResult> ImportAsync(Stream file, CancellationToken ct)
        {
            var result = new ImportResult();

            // create a progress id for UI to poll
            var progressId = Guid.NewGuid().ToString("N");
            _progressService.Start(progressId);
            result.ProgressId = progressId;

            using var package = new ExcelPackage(file);
            var ws = package.Workbook.Worksheets[0];
            if (ws == null)
            {
                result.Errors.Add("No worksheet found");
                _logger.LogWarning("Accounting import: no worksheet found");
                _progressService.Complete(progressId);
                return result;
            }

            // Accounting header is at row 5
            int headerRow = 5;
            int firstDataRow = headerRow + 1;

            // Map column indexes by header names
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
                _progressService.Complete(progressId);
                return result;
            }

            var batch = new List<AccountingRowDto>(capacity: 1024);
            int batchSize = _config.GetValue<int>("Import:BatchSize", 1000);
            int lastRow = ws.Dimension?.End.Row ?? 0;
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
                    _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows);
                }
            }

            if (batch.Count > 0)
            {
                var inserted = await BulkInsertAsync(batch);
                result.InsertedRows += inserted;
                _logger.LogInformation("Inserted final batch of {BatchCount} rows, inserted {Inserted}", batch.Count, inserted);
                batch.Clear();
                _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows);
            }

            _logger.LogInformation("Accounting import complete. TotalRows={TotalRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}", result.TotalRows, result.InsertedRows, result.FailedRows);
            _progressService.Complete(progressId);
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

                // Attempt MySQL bulk loader
                var csb = new MySqlConnectionStringBuilder(connStr);
                bool allowLocalInfile = csb.AllowLoadLocalInfile;
                if (!allowLocalInfile)
                {
                    _logger.LogWarning("Connection string does not enable LOCAL INFILE (AllowLoadLocalInfile=false). MySqlBulkLoader will be skipped.");
                }

                using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync();

                var loader = new MySqlBulkLoader(conn)
                {
                    TableName = "AccountingData",
                    FileName = tempFile,
                    FieldTerminator = ",",
                    FieldQuotationCharacter = '"',
                    LineTerminator = "\n",
                    NumberOfLinesToSkip = 0,
                    Local = allowLocalInfile
                };
                loader.Columns.Add("AccountingClass");
                loader.Columns.Add("Date");
                loader.Columns.Add("Num");
                loader.Columns.Add("Amount");
                loader.Columns.Add("AccountNumber");
                loader.Columns.Add("Account");
                loader.Columns.Add("Type");
                loader.Columns.Add("DateCreated");

                int maxAttempts = _config.GetValue<int>("Import:MaxAttempts", 3);
                int attempt = 0;
                Exception? lastLoaderEx = null;
                bool loaderSucceeded = false;

                if (allowLocalInfile)
                {
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

                if (!allowLocalInfile)
                {
                    _logger.LogDebug("Skipping MySqlBulkLoader because LOCAL INFILE is disabled in connection string.");
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

                _logger.LogWarning(lastLoaderEx, "Bulk loader failed after {Attempts} attempts and multi-row insert also failed; will fall back to row-by-row insert and save failed CSV.", maxAttempts);

                try
                {
                    var errorDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "cya_import_errors");
                    Directory.CreateDirectory(errorDir);
                    var saved = Path.Combine(errorDir, $"acct_failed_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.csv");
                    File.Copy(tempFile, saved, true);
                    _logger.LogError("Saved failed bulk CSV to {Path}", saved);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save failed CSV");
                }

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
