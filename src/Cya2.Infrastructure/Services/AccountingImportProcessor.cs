using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Cya2.Application.Interfaces;
using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using ImportResult = Cya2.Application.Interfaces.ImportResult;

namespace Cya2.Infrastructure.Services
{
    internal sealed class AccountingImportProcessor : IImportProcessor
    {
        private readonly IConfiguration _config;
        private readonly ILogger<AccountingImportProcessor> _logger;
        private readonly IImportProgressService _progressService;
        private readonly IAccountingImportRepository _repository;
        private readonly IImportCacheInvalidator _cacheInvalidator;

        public string ImportType => "accounting";

        public AccountingImportProcessor(
            IConfiguration config,
            ILogger<AccountingImportProcessor> logger,
            IImportProgressService progressService,
            IAccountingImportRepository repository,
            IImportCacheInvalidator cacheInvalidator)
        {
            _config = config;
            _logger = logger;
            _progressService = progressService;
            _repository = repository;
            _cacheInvalidator = cacheInvalidator;
        }

        public Task<ImportResult> ProcessAsync(Stream file, string progressId, CancellationToken cancellationToken)
            => ProcessAsync(file, cancellationToken, progressId);

        private async Task<ImportResult> ProcessAsync(Stream file, CancellationToken ct, string progressId)
        {
            var result = new ImportResult { ProgressId = progressId };

            using var package = new ExcelPackage(file);
            var ws = package.Workbook.Worksheets[0];
            if (ws == null)
            {
                result.Errors.Add("No worksheet found");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.SetStatus(progressId, "No worksheet found");
                return result;
            }

            // Step 1: File Validation
            _progressService.AddStep(progressId, "File Validation", "Validating Excel file structure...");
            int headerRow = 5, firstDataRow = 6;
            var map = BuildColumnMap(ws, headerRow);

            string[] required = { "Class", "Date", "Num", "Amount", "Account #", "Account", "Type" };
            foreach (var col in required) if (!map.ContainsKey(col)) result.Errors.Add($"Missing column: {col}");

            if (result.Errors.Count > 0)
            {
                _progressService.CompleteStep(progressId, "File Validation", "Failed - Missing required columns");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.Complete(progressId);
                return result;
            }

            int totalDataRows = Math.Max(0, (ws.Dimension?.End.Row ?? 0) - firstDataRow + 1);
            _progressService.CompleteStep(progressId, "File Validation", "Completed", $"Found {totalDataRows:N0} rows to import");

            // Step 2: Data Analysis — find earliest date
            _progressService.AddStep(progressId, "Data Analysis", "Analyzing import data for date range...");
            DateTime? earliest = null;
            int lastRow = ws.Dimension?.End.Row ?? 0;
            for (int r = firstDataRow; r <= lastRow; r++)
            {
                if (ExcelParsingHelpers.TryParseDateUS(ws.Cells[r, map["Date"]]?.Text?.Trim(), out var d))
                    if (earliest == null || d < earliest.Value) earliest = d;
            }

            if (earliest.HasValue)
            {
                _progressService.CompleteStep(progressId, "Data Analysis", "Completed", $"Earliest date: {earliest.Value:yyyy-MM-dd}");
                _progressService.AddStep(progressId, "Database Backup", "Backing up all existing records...");
                await _repository.BackupAllAndDeleteFromDateAsync(earliest.Value, progressId, ct);
                _progressService.CompleteStep(progressId, "Database Backup", "Completed", "All existing data backed up, range cleared");
            }
            else
            {
                _progressService.CompleteStep(progressId, "Data Analysis", "Completed", "No valid dates found in import file");
            }

            // Step 3: Data Import
            _progressService.AddStep(progressId, "Data Import", "Importing accounting records...");
            _progressService.SetExpected(progressId, totalDataRows);
            _progressService.Report(progressId, 0, 0, 0, "Starting import...");

            int batchSize = _config.GetValue<int>("Import:BatchSize", 1000);
            var batch = new List<AccountingImportRowDto>(batchSize);

            for (int r = firstDataRow; r <= lastRow; r++)
            {
                if (ct.IsCancellationRequested) break;

                var cls = ws.Cells[r, map["Class"]]?.Text?.Trim();
                var dtTxt = ws.Cells[r, map["Date"]]?.Text?.Trim();
                var num = ws.Cells[r, map["Num"]]?.Text?.Trim();
                var amtTxt = ws.Cells[r, map["Amount"]]?.Text?.Trim();
                var acct = ws.Cells[r, map["Account"]]?.Text?.Trim();

                if (string.IsNullOrWhiteSpace(cls) && string.IsNullOrWhiteSpace(num) && string.IsNullOrWhiteSpace(acct)) continue;

                if (!ExcelParsingHelpers.TryParseDateUS(dtTxt, out var date)) { result.FailedRows++; result.Errors.Add($"Row {r}: invalid Date '{dtTxt}'"); continue; }
                if (!ExcelParsingHelpers.TryParseDoubleUS(amtTxt, out var amount)) { result.FailedRows++; result.Errors.Add($"Row {r}: invalid Amount '{amtTxt}'"); continue; }

                batch.Add(new AccountingImportRowDto
                {
                    AccountingClass = cls ?? string.Empty,
                    Date = date,
                    Num = num ?? string.Empty,
                    Amount = amount,
                    AccountNumber = ws.Cells[r, map["Account #"]]?.Text?.Trim() ?? string.Empty,
                    Account = acct ?? string.Empty,
                    Type = ws.Cells[r, map["Type"]]?.Text?.Trim() ?? string.Empty
                });
                result.TotalRows++;

                if (batch.Count >= batchSize)
                {
                    var batchResult = await _repository.BulkInsertAsync(batch, ct);
                    result.InsertedRows += batchResult.Inserted;
                    result.Errors.AddRange(batchResult.Errors);
                    batch.Clear();
                    _progressService.Report(progressId, result.TotalRows, result.InsertedRows, result.FailedRows, "Importing data...");
                }
            }

            if (batch.Count > 0)
            {
                var batchResult = await _repository.BulkInsertAsync(batch, ct);
                result.InsertedRows += batchResult.Inserted;
                result.Errors.AddRange(batchResult.Errors);
            }

            _progressService.CompleteStep(progressId, "Data Import", "Completed", $"{result.InsertedRows:N0} records imported, {result.FailedRows:N0} failed");
            _progressService.AddErrors(progressId, result.Errors);
            _progressService.Complete(progressId);

            _cacheInvalidator.InvalidateAll();

            _logger.LogInformation("Accounting import complete. Total={Total} Inserted={Inserted} Failed={Failed}", result.TotalRows, result.InsertedRows, result.FailedRows);

            return result;
        }

        private static Dictionary<string, int> BuildColumnMap(OfficeOpenXml.ExcelWorksheet ws, int headerRow)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.Dimension?.End.Column ?? 0;
            for (int c = 1; c <= lastCol; c++)
            {
                var h = ws.Cells[headerRow, c]?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(h) && !map.ContainsKey(h)) map[h] = c;
            }
            return map;
        }
    }
}
