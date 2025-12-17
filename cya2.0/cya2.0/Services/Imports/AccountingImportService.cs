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
    internal sealed class AccountingImportService : IAccountingImportService
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;
        private readonly ILogger<AccountingImportService> _logger;

        public AccountingImportService(IDataAccess data, IConfiguration config, ILogger<AccountingImportService> logger)
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

            string[] required = ["Class", "Date", "Num", "Amount", "Account #", "Account", "Type"]; 
            foreach (var col in required)
            {
                if (!map.ContainsKey(col))
                {
                    result.Errors.Add($"Missing column: {col}");
                }
            }
            if (result.Errors.Count > 0) return result;

            var batch = new List<AccountingRowDto>(capacity: 1024);
            int lastRow = ws.Dimension?.End.Row ?? 0;

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
                {
                    continue; // skip blank line
                }

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

        private async Task<int> BulkInsertAsync(List<AccountingRowDto> batch)
        {
            if (batch.Count == 0) return 0;
            const string sql = @"INSERT INTO AccountingData
                (AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated)
                VALUES (@AccountingClass, @Date, @Num, @Amount, @AccountNumber, @Account, @Type, @DateCreated)";

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
                _logger.LogError(ex, "Accounting bulk insert failed");
                return 0;
            }
        }
    }
}
