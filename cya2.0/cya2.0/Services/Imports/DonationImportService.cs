using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace cya2.Services.Imports
{
    internal sealed class DonationImportService : IDonationImportService
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<DonationImportService> _logger;

        public DonationImportService(IDataAccess data, IConfiguration config, ILogger<DonationImportService> logger)
        {
            _data = data;
            _config = config;
            _logger = logger;
        }

        public async Task<ImportResult> ImportAsync(Stream file, CancellationToken ct)
        {
            var result = new ImportResult();

            using var package = new ExcelPackage(file);
            var ws = package.Workbook.Worksheets[0];
            if (ws == null)
            {
                result.Errors.Add("No worksheet found");
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

            string[] required = [
                "Gift Date","Name","Gift Payment Type","Gift Type","Fund Split Amount","Fund Notes",
                "Soft Credit Recipient Name","Preferred Address Line 1","Preferred City","Preferred State",
                "Preferred ZIP","Preferred Country","Personal Email Number","Home Phone Number",
                "Personal Mobile Phone Number","Gift Is Anonymous"
            ];
            foreach (var col in required)
            {
                if (!map.ContainsKey(col))
                {
                    result.Errors.Add($"Missing column: {col}");
                }
            }
            if (result.Errors.Count > 0) return result;

            var batch = new List<DonationRowDto>(capacity: 1024);
            int lastRow = ws.Dimension?.End.Row ?? 0;

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

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fund) && string.IsNullOrWhiteSpace(amountTxt))
                {
                    continue; // skip blank line
                }

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
                    SoftCreditName = soft,
                    Address = addr,
                    City = city,
                    State = state,
                    PostalCode = zip,
                    Country = country,
                    Email = email,
                    PhoneFixed = phone,
                    PhoneMobile = mobile,
                    IsAnonymous = ExcelParsingHelpers.ParseYesNo(anonTxt),
                    DateCreated = DateTime.UtcNow
                });

                result.TotalRows++;

                if (batch.Count >= 1000)
                {
                    result.InsertedRows += await BulkInsertAsync(batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                result.InsertedRows += await BulkInsertAsync(batch);
                batch.Clear();
            }

            return result;
        }

        private async Task<int> BulkInsertAsync(List<DonationRowDto> batch)
        {
            if (batch.Count == 0) return 0;
            const string sql = @"INSERT INTO DonationData
                (Date, AccountName, PaymentMethod, GiftType, Amount, Fund, SoftCreditName, Address, City, State, PostalCode, Country, Email, PhoneFixed, PhoneMobile, DateCreated, IsAnonymous)
                VALUES (@Date, @AccountName, @PaymentMethod, @GiftType, @Amount, @Fund, @SoftCreditName, @Address, @City, @State, @PostalCode, @Country, @Email, @PhoneFixed, @PhoneMobile, @DateCreated, @IsAnonymous)";

            try
            {
                var conn = _config.GetConnectionString("default");
                int inserted = 0;
                foreach (var row in batch)
                {
                    inserted += await _data.SaveData(sql, row, conn);
                }
                return inserted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Donation bulk insert failed");
                return 0;
            }
        }
    }
}
