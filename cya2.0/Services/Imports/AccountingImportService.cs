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

namespace cya2.Services.Imports
{
    internal sealed class AccountingImportService : IAccountingImportService
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Data, string FileName, string ContentType, DateTime CreatedAt)> _previews = new();

        private readonly IConfiguration _config;
        private readonly ILogger<AccountingImportService> _logger;
        private readonly IImportProgressService _progressService;
        private readonly IAccountingImportRepository _repository;
        private readonly ISessionDashboardDtoCacheService _dashboardCache;
        private readonly IImportCacheInvalidator _cacheInvalidator;

        public AccountingImportService(
            IConfiguration config,
            ILogger<AccountingImportService> logger,
            IImportProgressService progressService,
            IAccountingImportRepository repository,
            ISessionDashboardDtoCacheService dashboardCache,
            IImportCacheInvalidator cacheInvalidator)
        {
            _config = config;
            _logger = logger;
            _progressService = progressService;
            _repository = repository;
            _dashboardCache = dashboardCache;
            _cacheInvalidator = cacheInvalidator;
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

            byte[] data;
            using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); data = ms.ToArray(); }

            _ = Task.Run(async () =>
            {
                try { using var ms = new MemoryStream(data, writable: false); await ProcessAsync(ms, CancellationToken.None, progressId); }
                catch (Exception ex) { _progressService.SetStatus(progressId, $"Error: {ex.Message}"); }
            });

            return result;
        }

        public async Task<FilePreviewResult> PreviewAsync(Stream file, string fileName, string contentType, CancellationToken ct)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            byte[] data;
            using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); data = ms.ToArray(); }

            var previewId = Guid.NewGuid().ToString("N");
            _previews[previewId] = (data, fileName, contentType, DateTime.UtcNow);
            _logger.LogInformation("Created accounting preview {PreviewId} for {FileName} ({Size} bytes)", previewId, fileName, data.LongLength);

            return new FilePreviewResult { PreviewId = previewId, FileName = fileName ?? string.Empty, FileSizeBytes = data.LongLength, ContentType = contentType ?? string.Empty };
        }

        public async Task<ImportResult> ImportFromPreviewAsync(string previewId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(previewId)) throw new ArgumentException("PreviewId is required", nameof(previewId));
            if (!_previews.TryRemove(previewId, out var entry))
            {
                _logger.LogWarning("Accounting preview {PreviewId} not found or expired", previewId);
                var res = new ImportResult(); res.Errors.Add("Preview session expired. Please upload the file again."); return res;
            }
            using var ms = new MemoryStream(entry.Data, writable: false);
            return await ImportAsync(ms, ct);
        }

        public Task<ImportResult> StartImportFromPreviewAsync(string previewId, string progressId)
        {
            var result = new ImportResult();
            var pId = string.IsNullOrWhiteSpace(progressId) ? Guid.NewGuid().ToString("N") : progressId;
            _progressService.Start(pId, "Accounting");
            result.ProgressId = pId;

            if (string.IsNullOrWhiteSpace(previewId)) { result.Errors.Add("PreviewId is required"); _progressService.SetStatus(pId, "PreviewId is required"); return Task.FromResult(result); }
            if (!_previews.TryRemove(previewId, out var entry)) { result.Errors.Add("Preview session expired. Please upload the file again."); _progressService.SetStatus(pId, "Preview session expired"); return Task.FromResult(result); }

            var data = entry.Data;
            _ = Task.Run(async () =>
            {
                try { using var ms = new MemoryStream(data, writable: false); await ProcessAsync(ms, CancellationToken.None, pId); }
                catch (Exception ex) { _progressService.SetStatus(pId, $"Error: {ex.Message}"); _logger.LogError(ex, "Background accounting import failed for preview {PreviewId}", previewId); }
            });

            return Task.FromResult(result);
        }

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
