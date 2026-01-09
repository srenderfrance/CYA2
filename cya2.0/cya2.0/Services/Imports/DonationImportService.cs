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
    internal sealed class DonationImportService : IDonationImportService
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<DonationImportService> _logger;
        private readonly ImportProgressService _progressService;

        public DonationImportService(IDataAccess data, IConfiguration config, ILogger<DonationImportService> logger, ImportProgressService progressService)
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
                _logger.LogWarning("Donation import: no worksheet found");
                _progressService.Complete(progressId);
                return result;
            }

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
                _progressService.Complete(progressId);
                return result;
            }

            var batch = new List<DonationRowDto>(capacity: 1024);
            int batchSize = _config.GetValue<int>("Import:BatchSize", 1000);
            int lastRow = ws.Dimension?.End.Row ?? 0;
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
                    _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows);
                }
            }

            if (batch.Count > 0)
            {
                var inserted = await BulkInsertAsync(batch);
                result.InsertedRows += inserted;
                _logger.LogInformation("Inserted final batch of {BatchCount} donation rows, inserted {Inserted}", batch.Count, inserted);
                batch.Clear();
                _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows);
            }

            _logger.LogInformation("Donation import complete. TotalRows={TotalRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}", result.TotalRows, result.InsertedRows, result.FailedRows);
            _progressService.Complete(progressId);
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
                bool allowLocalInfile = csb.AllowLoadLocalInfile;
                if (!allowLocalInfile)
                {
                    _logger.LogWarning("Connection string does not enable LOCAL INFILE (AllowLoadLocalInfile=false). MySqlBulkLoader will be skipped for donations.");
                }

                using var conn = new MySqlConnection(csb.ConnectionString);
                await conn.OpenAsync();

                var loader = new MySqlBulkLoader(conn)
                {
                    TableName = "DonationData",
                    FileName = tempFile,
                    FieldTerminator = ",",
                    FieldQuotationCharacter = '"',
                    LineTerminator = "\n",
                    NumberOfLinesToSkip = 0,
                    Local = allowLocalInfile,
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

                int maxAttempts = _config.GetValue<int>("Import:MaxAttempts", 3);
                int attempt = 0;
                Exception? lastLoaderEx = null;
                if (allowLocalInfile)
                {
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

                // Try multi-row insert with retries
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

                _logger.LogWarning(lastLoaderEx, "Bulk loader failed after {Attempts} attempts and multi-row insert also failed for donations; will fall back to row-by-row insert and save failed CSV.", maxAttempts);

                // Save failed CSV for diagnostics
                try
                {
                    var errorDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "cya_import_errors");
                    Directory.CreateDirectory(errorDir);
                    var saved = Path.Combine(errorDir, $"donation_failed_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.csv");
                    File.Copy(tempFile, saved, true);
                    _logger.LogError("Saved failed donations bulk CSV to {Path}", saved);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save failed donations CSV");
                }

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
    }
}
