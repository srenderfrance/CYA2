using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Cya2.Application.Interfaces;
using Cya2.Core.DTOs;
using Cya2.Core.Interfaces;
using Cya2.Core.Services;
using ImportResult = Cya2.Application.Interfaces.ImportResult;

namespace Cya2.Infrastructure.Services
{
    internal sealed class DonationImportProcessor : IDonationImportMaintenanceService, IImportProcessor
    {
        private readonly IConfiguration _config;
        private readonly ILogger<DonationImportProcessor> _logger;
        private readonly IImportProgressService _progressService;
        private readonly IDonationImportRepository _repository;
        private readonly IImportCacheInvalidator _cacheInvalidator;

        public string ImportType => "donations";

        public DonationImportProcessor(
            IConfiguration config,
            ILogger<DonationImportProcessor> logger,
            IImportProgressService progressService,
            IDonationImportRepository repository,
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

        public async Task<DonorNameNormalizationResult> NormalizeExistingDonorNamesAsync(CancellationToken ct)
        {
            var (donationDataUpdated, donationBackupUpdated) = await _repository.NormalizeExistingDonorNamesAsync(ct);
            _cacheInvalidator.InvalidateAll();

            return new DonorNameNormalizationResult
            {
                DonationDataRowsUpdated = donationDataUpdated,
                DonationDataBackupRowsUpdated = donationBackupUpdated
            };
        }

        public async Task<DonationRecategorizationResult> RecategorizeAllDonationsAsync(CancellationToken ct)
        {
            var updated = await _repository.RecategorizeAllDonationsAsync(ct);
            _cacheInvalidator.InvalidateAll();

            return new DonationRecategorizationResult
            {
                DonationDataRowsUpdated = updated
            };
        }

        private async Task<ImportResult> ProcessAsync(Stream file, CancellationToken ct, string progressId)
        {
            var result = new ImportResult { ProgressId = progressId };
            var frequencyService = new DonorFrequencyService();

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
            int headerRow = 1, firstDataRow = 2;
            var map = BuildColumnMap(ws, headerRow);

            string[] required = { "Gift Date","Name","Gift Payment Type","Gift Type","Fund Split Amount","Fund Notes",
                "Honor/Memorial Name",
                "Soft Credit Recipient Name","Preferred Address Line 1","Preferred City","Preferred State",
                "Preferred ZIP","Preferred Country","Personal Email Number","Home Phone Number",
                "Personal Mobile Phone Number","Gift Is Anonymous" };
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

            // Step 2: Full parse into memory
            _progressService.AddStep(progressId, "Data Analysis", "Parsing and analysing import data...");
            int lastRow = ws.Dimension?.End.Row ?? 0;

            var allRows = new List<DonationImportRowDto>(totalDataRows);
            DateTime? earliest = null;

            for (int r = firstDataRow; r <= lastRow; r++)
            {
                var dateTxt = ws.Cells[r, map["Gift Date"]]?.Text?.Trim();
                var name    = ws.Cells[r, map["Name"]]?.Text?.Trim();
                var amountTxt = ws.Cells[r, map["Fund Split Amount"]]?.Text?.Trim();
                var fund    = ws.Cells[r, map["Fund Notes"]]?.Text?.Trim();
                var internDesignation = GetCellText(ws, r, map["Honor/Memorial Name"]);
                var addressee = map.TryGetValue("Primary Addressee", out var primaryAddresseeCol)
                    ? GetCellText(ws, r, primaryAddresseeCol)
                    : map.TryGetValue("Addressee", out var addresseeCol)
                        ? GetCellText(ws, r, addresseeCol)
                        : null;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(fund) && string.IsNullOrWhiteSpace(amountTxt)) continue;
                if (!ExcelParsingHelpers.TryParseDateUS(dateTxt, out var date)) { result.FailedRows++; result.Errors.Add($"Row {r}: invalid Gift Date '{dateTxt}'"); continue; }
                if (!ExcelParsingHelpers.TryParseDoubleUS(amountTxt, out var amount)) { result.FailedRows++; result.Errors.Add($"Row {r}: invalid Amount '{amountTxt}'"); continue; }

                if (earliest == null || date < earliest.Value) earliest = date;

                var isAnon = ExcelParsingHelpers.ParseYesNo(ws.Cells[r, map["Gift Is Anonymous"]]?.Text?.Trim());

                allRows.Add(new DonationImportRowDto
                {
                    Date          = date,
                    AccountName   = isAnon ? "Anonymous" : NormalizeDonorName(name),
                    PaymentMethod = ws.Cells[r, map["Gift Payment Type"]]?.Text?.Trim() ?? string.Empty,
                    GiftType      = ws.Cells[r, map["Gift Type"]]?.Text?.Trim() ?? string.Empty,
                    Amount        = amount,
                    Fund          = fund ?? string.Empty,
                    Intern        = internDesignation,
                    Addressee     = addressee,
                    SoftCreditName = isAnon ? null : ws.Cells[r, map["Soft Credit Recipient Name"]]?.Text?.Trim(),
                    Address       = isAnon ? null : ws.Cells[r, map["Preferred Address Line 1"]]?.Text?.Trim(),
                    City          = isAnon ? null : ws.Cells[r, map["Preferred City"]]?.Text?.Trim(),
                    State         = isAnon ? null : ws.Cells[r, map["Preferred State"]]?.Text?.Trim(),
                    PostalCode    = isAnon ? null : ws.Cells[r, map["Preferred ZIP"]]?.Text?.Trim(),
                    Country       = isAnon ? null : ws.Cells[r, map["Preferred Country"]]?.Text?.Trim(),
                    Email         = isAnon ? null : ws.Cells[r, map["Personal Email Number"]]?.Text?.Trim(),
                    PhoneFixed    = isAnon ? null : ws.Cells[r, map["Home Phone Number"]]?.Text?.Trim(),
                    PhoneMobile   = isAnon ? null : ws.Cells[r, map["Personal Mobile Phone Number"]]?.Text?.Trim(),
                    IsAnonymous   = isAnon
                });
                result.TotalRows++;
            }

            var internPopulatedCount = allRows.Count(r => !string.IsNullOrWhiteSpace(r.Intern));
            var addresseePopulatedCount = allRows.Count(r => !string.IsNullOrWhiteSpace(r.Addressee));
            _logger.LogInformation(
                "Donation import parse debug: rows={Rows}, internPopulated={InternPopulated}, addresseePopulated={AddresseePopulated}, internColumnIndex={InternColumnIndex}, addresseeColumnIndex={AddresseeColumnIndex}",
                allRows.Count,
                internPopulatedCount,
                addresseePopulatedCount,
                map["Honor/Memorial Name"],
                map.TryGetValue("Primary Addressee", out var primaryAddresseeIndex)
                    ? primaryAddresseeIndex
                    : (map.TryGetValue("Addressee", out var addresseeIndex) ? addresseeIndex : -1));

            var internSamples = allRows
                .Select(r => r.Intern)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();

            if (internSamples.Length > 0)
            {
                _logger.LogInformation("Donation import intern samples: {Samples}", string.Join(" | ", internSamples));
            }

            _progressService.CompleteStep(progressId, "Data Analysis", "Completed", $"Earliest date: {earliest?.ToString("yyyy-MM-dd") ?? "none"}");

            // Step 3: Fetch prior donor history from DB
            _progressService.AddStep(progressId, "Frequency Pre-load", "Fetching prior donor history for frequency classification...");

            var donorKeys = allRows
                .Where(r => !string.IsNullOrWhiteSpace(r.AccountName) && !string.IsNullOrWhiteSpace(r.Fund))
                .Select(r => (r.AccountName, r.Fund))
                .Distinct()
                .ToList();

            // Fetch last 26 months of prior gifts per donor (enough for yearly + catch-up detection).
            var priorHistory = earliest.HasValue
                ? await _repository.GetRecentDonationsForDonorsAsync(donorKeys, earliest.Value, 30, ct)
                : new List<Cya2.Core.ReadModels.DonationRecord>();

            _progressService.CompleteStep(progressId, "Frequency Pre-load", "Completed",
                $"Loaded prior history for {priorHistory.Select(h => h.AccountName).Distinct().Count()} donors");

            // Step 4: Backup + delete overlapping range
            if (earliest.HasValue)
            {
                _progressService.AddStep(progressId, "Database Backup", "Backing up all existing records...");
                await _repository.BackupAllAndDeleteFromDateAsync(earliest.Value, progressId, ct);
                _progressService.CompleteStep(progressId, "Database Backup", "Completed", "All existing data backed up, range cleared");
            }

            // Step 5: Classify each row using merged history
            _progressService.AddStep(progressId, "Frequency Classification", "Classifying donor frequency per row...");

            // Build a lookup: (accountName.lower, fund.lower) -> sorted prior gift records
            var priorByDonor = priorHistory
                .GroupBy(h => (h.AccountName.ToLowerInvariant(), h.Fund.ToLowerInvariant()))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.Date)
                          .Select(x => new DonorGiftRecord { Date = x.Date, Amount = Convert.ToDecimal(x.Amount) })
                          .ToList());

            // Group file rows by donor and classify each gift with full merged context.
            var rowsByDonor = allRows
                .Where(r => !string.IsNullOrWhiteSpace(r.AccountName))
                .GroupBy(r => (r.AccountName.ToLowerInvariant(), r.Fund.ToLowerInvariant()));

            foreach (var group in rowsByDonor)
            {
                priorByDonor.TryGetValue(group.Key, out var prior);
                prior ??= new List<DonorGiftRecord>();

                // File rows sorted ascending for this donor.
                var fileGifts = group.OrderBy(r => r.Date).ToList();

                // Full combined history for context (prior + file).
                var allGifts = prior
                    .Concat(fileGifts.Select(r => new DonorGiftRecord { Date = r.Date, Amount = Convert.ToDecimal(r.Amount) }))
                    .OrderBy(g => g.Date)
                    .ToList();

                // Determine the new-data cutoff: the latest date already in the DB for this donor.
                var priorCutoff = prior.Any() ? prior.Max(p => p.Date) : DateTime.MinValue;

                foreach (var row in fileGifts)
                {
                    // Skip reclassification for rows that predate the new data and already
                    // have a stored frequency (unchanged rows from the overlap year).
                    // We still re-insert them, but carry forward their existing classification.
                    // For truly new rows (after priorCutoff) always classify fresh.
                    if (row.Date <= priorCutoff)
                    {
                        // Look up stored frequency from the prior history record if available.
                        var matchingPrior = priorHistory.FirstOrDefault(h =>
                            string.Equals(h.AccountName, row.AccountName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(h.Fund, row.Fund, StringComparison.OrdinalIgnoreCase) &&
                            h.Date.Date == row.Date.Date &&
                            h.Frequency.HasValue);

                        if (matchingPrior != null)
                        {
                            row.Frequency = matchingPrior.Frequency;
                            continue;
                        }
                    }

                    // Classify using DonorFrequencyService with full merged history as context.
                    var giftRecord = new DonorGiftRecord { Date = row.Date, Amount = Convert.ToDecimal(row.Amount) };
                    var classification = frequencyService.ClassifyGift(giftRecord, allGifts);
                    row.Frequency = classification.Frequency;
                }
            }

            _progressService.CompleteStep(progressId, "Frequency Classification", "Completed");

            // Step 6: Bulk insert with Frequency populated
            _progressService.AddStep(progressId, "Data Import", "Importing donation records...");
            _progressService.SetExpected(progressId, allRows.Count);
            _progressService.Report(progressId, 0, 0, 0, "Starting import...");

            int batchSize = _config.GetValue<int>("Import:BatchSize", 1000);
            int inserted = 0;

            for (int i = 0; i < allRows.Count; i += batchSize)
            {
                if (ct.IsCancellationRequested) break;
                var batch = allRows.Skip(i).Take(batchSize).ToList();
                var batchResult = await _repository.BulkInsertAsync(batch, ct);
                inserted += batchResult.Inserted;
                result.InsertedRows += batchResult.Inserted;
                result.Errors.AddRange(batchResult.Errors);
                _progressService.Report(progressId, Math.Min(i + batchSize, allRows.Count), inserted, result.FailedRows, "Importing data...");
            }

            _progressService.CompleteStep(progressId, "Data Import", "Completed",
                $"{result.InsertedRows:N0} records imported, {result.FailedRows:N0} failed");
            _progressService.AddErrors(progressId, result.Errors);
            _progressService.Complete(progressId);

            _cacheInvalidator.InvalidateAll();

            _logger.LogInformation("Donation import complete. Total={Total} Inserted={Inserted} Failed={Failed}",
                result.TotalRows, result.InsertedRows, result.FailedRows);

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

        private static string? GetCellText(OfficeOpenXml.ExcelWorksheet ws, int row, int column)
        {
            var cell = ws.Cells[row, column];
            var text = cell?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var value = cell?.Value?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string NormalizeDonorName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            var normalized = string.Join(" ", rawName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Convert "LastName, FirstName" (or "LastName, First Middle") to "FirstName LastName"
            // so donor names are stored consistently.
            if (normalized.Contains(','))
            {
                var parts = normalized
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

                if (parts.Count >= 2)
                {
                    var lastName = parts[0];
                    var firstNamePart = parts[1];
                    var trailingParts = parts.Count > 2
                        ? string.Join(' ', parts.Skip(2))
                        : string.Empty;

                    var reordered = string.IsNullOrWhiteSpace(trailingParts)
                        ? $"{firstNamePart} {lastName}"
                        : $"{firstNamePart} {lastName} {trailingParts}";

                    return string.Join(" ", reordered
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }

            return normalized;
        }
    }
}
