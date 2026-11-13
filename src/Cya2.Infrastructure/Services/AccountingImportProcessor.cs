using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Cya2.Application.Interfaces;
using Cya2.Application.Diagnostics;
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

        public async Task<ImportResult> ProcessAsync(Stream file, string progressId, CancellationToken cancellationToken)
        {
            using var lease = await FinancialWriteCoordinator.EnterAsync(cancellationToken);
            try { return await ProcessAsync(file, cancellationToken, progressId); }
            catch (ImportOutcomeUnknownException)
            {
                try { _cacheInvalidator.Invalidate(CacheDataDomains.Accounting); }
                catch { _logger.LogWarning("Uncertain accounting import cache refresh failed."); }
                throw;
            }
        }

        private async Task<ImportResult> ProcessAsync(Stream file, CancellationToken ct, string progressId)
        {
            return await ProcessImportCoreAsync(file, ct, progressId);
        }

        private async Task<ImportResult> ProcessImportCoreAsync(Stream file, CancellationToken ct, string progressId)
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

            _logger.LogInformation(
                "Accounting import header validation: headerRow={HeaderRow} worksheetData={WorksheetData}",
                headerRow,
                ws.Dimension is not null);

            string[] required = { "Class", "Date", "Num", "Amount", "Account #", "Account", "Type" };
            foreach (var col in required)
            {
                if (!map.ContainsKey(col))
                {
                    _logger.LogWarning(
                        "Accounting import missing required column: expected={ExpectedColumn} headerRow={HeaderRow}",
                        col,
                        headerRow);
                    result.Errors.Add($"Missing column: {col}");
                }
            }

            if (result.Errors.Count > 0)
            {
                _progressService.CompleteStep(progressId, "File Validation", "Failed - Missing required columns");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.Complete(progressId);
                return result;
            }

            int lastRow = ws.Dimension?.End.Row ?? 0;
            while (lastRow >= firstDataRow && IsBlankRow(ws, lastRow)) lastRow--;
            if (lastRow >= firstDataRow + 2 && IsBlankRow(ws, lastRow - 1) && IsBlankRow(ws, lastRow - 2) && IsCashBasisFooter(ws, lastRow))
            {
                lastRow--;
                while (lastRow >= firstDataRow && IsBlankRow(ws, lastRow)) lastRow--;
            }

            int totalDataRows = Math.Max(0, lastRow - firstDataRow + 1);
            _progressService.CompleteStep(progressId, "File Validation", "Completed", $"Found {totalDataRows:N0} rows to import");

            _progressService.AddStep(progressId, "Data Analysis", "Validating all accounting rows before publication...");

            // Step 3: Data Import
            _progressService.AddStep(progressId, "Data Import", "Importing accounting records...");
            _progressService.SetExpected(progressId, totalDataRows);
            _progressService.Report(progressId, 0, 0, 0, "Starting import...");

            var batch = new List<AccountingImportRowDto>(totalDataRows);

            for (int r = firstDataRow; r <= lastRow; r++)
            {
                ct.ThrowIfCancellationRequested();

                var cls = ws.Cells[r, map["Class"]]?.Text?.Trim();
                var dtTxt = ws.Cells[r, map["Date"]]?.Text?.Trim();
                var num = ws.Cells[r, map["Num"]]?.Text?.Trim();
                var amtTxt = ws.Cells[r, map["Amount"]]?.Text?.Trim();
                var acct = ws.Cells[r, map["Account"]]?.Text?.Trim();

                if (IsBlankRow(ws, r)) continue;

                if (!ExcelParsingHelpers.TryParseDateUS(dtTxt, out var date)) { result.FailedRows++; result.Errors.Add(ImportDiagnosticMessages.InvalidCell(r, "Date")); continue; }
                if (!ExcelParsingHelpers.TryParseDoubleUS(amtTxt, out var amount)) { result.FailedRows++; result.Errors.Add(ImportDiagnosticMessages.InvalidCell(r, "Amount")); continue; }
                if (!double.IsFinite(amount) || Math.Abs(amount) >= 10000000000000000d || string.IsNullOrWhiteSpace(cls) || string.IsNullOrWhiteSpace(acct))
                {
                    result.FailedRows++;
                    result.Errors.Add(ImportDiagnosticMessages.InvalidCell(r, "Amount, Class or Account"));
                    continue;
                }

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

            }

            if (result.Errors.Count > 0 || batch.Count == 0)
            {
                if (result.Errors.Count == 0) result.Errors.Add("No accounting rows found. Existing data was not changed.");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.SetStatus(progressId, "Rejected - existing data was not changed");
                _progressService.Complete(progressId);
                return result;
            }

            _progressService.CompleteStep(progressId, "Data Analysis", "Completed", $"Validated {batch.Count:N0} accounting rows");
            ct.ThrowIfCancellationRequested();
            var published = await _repository.ReplaceAsync(batch, ct);
            result.InsertedRows = published.Inserted;
            ImportCompletion.Notify(result, progressId, CacheDataDomains.Accounting, _cacheInvalidator, _progressService, _logger);

            _logger.LogInformation("Accounting import complete. Total={Total} Inserted={Inserted} Failed={Failed}", result.TotalRows, result.InsertedRows, result.FailedRows);

            return result;
        }

        private static bool IsBlankRow(ExcelWorksheet ws, int row)
            => Enumerable.Range(1, ws.Dimension?.End.Column ?? 0).All(column => string.IsNullOrWhiteSpace(ws.Cells[row, column].Text));

        private static bool IsCashBasisFooter(ExcelWorksheet ws, int row)
        {
            var cells = Enumerable.Range(1, ws.Dimension?.End.Column ?? 0)
                .Select(column => ws.Cells[row, column].Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(2)
                .ToArray();
            return cells.Length == 1 && cells[0].EndsWith("Cash Basis", StringComparison.OrdinalIgnoreCase);
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
