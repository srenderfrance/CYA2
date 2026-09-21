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
        private readonly IDonationRepository _donationRepository;
        private readonly IImportCacheInvalidator _cacheInvalidator;
        private readonly DonorFrequencyService _frequencyService;
        private readonly DonorIdentityResolver _identityResolver;

        public string ImportType => "donations";

        public DonationImportProcessor(
            IConfiguration config,
            ILogger<DonationImportProcessor> logger,
            IImportProgressService progressService,
            IDonationRepository donationRepository,
            IImportCacheInvalidator cacheInvalidator,
            DonorFrequencyService frequencyService,
            DonorIdentityResolver identityResolver)
        {
            _config = config;
            _logger = logger;
            _progressService = progressService;
            _donationRepository = donationRepository;
            _cacheInvalidator = cacheInvalidator;
            _frequencyService = frequencyService;
            _identityResolver = identityResolver;
        }

        public Task<ImportResult> ProcessAsync(Stream file, string progressId, CancellationToken cancellationToken)
            => ProcessAsync(file, cancellationToken, progressId);

        public async Task<DonationRecategorizationResult> RecategorizeAllDonationsAsync(CancellationToken ct)
        {
            var updated = await _donationRepository.RecategorizeAllDonationsAsync(ct);
            _cacheInvalidator.InvalidateAll();
            return new DonationRecategorizationResult { DonationDataRowsUpdated = updated };
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

            _progressService.AddStep(progressId, "File Validation", "Validating Excel file structure...");
            const int headerRow = 1;
            const int firstDataRow = 2;
            var map = BuildColumnMap(ws, headerRow);
            string[] required =
            {
                "Gift Date", "Name", "Gift Payment Type", "Gift Type", "Fund Split Amount", "Fund Notes",
                "Soft Credit Recipient Name", "Preferred Address Line 1", "Preferred City", "Preferred State",
                "Preferred ZIP", "Preferred Country", "Personal Email Number", "Home Phone Number",
                "Personal Mobile Phone Number", "Gift Is Anonymous", "Gift Import ID",
                "Primary Addressee"
            };
            foreach (var column in required)
            {
                if (!map.ContainsKey(column))
                {
                    result.Errors.Add($"Missing column: {column}");
                }
            }

            var honorMemorialColumn = GetFirstAvailableColumn(
                map,
                "Honor/Memorial Sort Name",
                "Honor/Memorial Name");
            if (honorMemorialColumn is null)
            {
                result.Errors.Add("Missing column: Honor/Memorial Sort Name or Honor/Memorial Name");
            }

            if (result.Errors.Count > 0)
            {
                _progressService.CompleteStep(progressId, "File Validation", "Failed - Missing required columns");
                _progressService.AddErrors(progressId, result.Errors);
                _progressService.Complete(progressId);
                return result;
            }

            var totalDataRows = Math.Max(0, (ws.Dimension?.End.Row ?? 0) - firstDataRow + 1);
            _progressService.CompleteStep(progressId, "File Validation", "Completed", $"Found {totalDataRows:N0} rows to import");

            _progressService.AddStep(progressId, "Data Analysis", "Parsing, resolving donor identity, and consolidating duplicates...");
            var allRows = new List<DonationImportRowDto>(totalDataRows);
            var earliest = (DateTime?)null;
            var lastRow = ws.Dimension?.End.Row ?? 0;

            for (var rowNumber = firstDataRow; rowNumber <= lastRow; rowNumber++)
            {
                var dateText = GetCellText(ws, rowNumber, map["Gift Date"]);
                var accountName = GetCellText(ws, rowNumber, map["Name"]);
                var amountText = GetCellText(ws, rowNumber, map["Fund Split Amount"]);
                var fund = GetCellText(ws, rowNumber, map["Fund Notes"]);
                if (string.IsNullOrWhiteSpace(accountName) && string.IsNullOrWhiteSpace(fund) && string.IsNullOrWhiteSpace(amountText))
                {
                    continue;
                }

                if (!ExcelParsingHelpers.TryParseDateUS(dateText, out var date))
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: invalid Gift Date '{dateText}'");
                    continue;
                }

                if (!ExcelParsingHelpers.TryParseDoubleUS(amountText, out var amount))
                {
                    result.FailedRows++;
                    result.Errors.Add($"Row {rowNumber}: invalid Amount '{amountText}'");
                    continue;
                }

                var isAnonymous = ExcelParsingHelpers.ParseYesNo(GetCellText(ws, rowNumber, map["Gift Is Anonymous"]));
                var addressee = GetCellText(ws, rowNumber, map["Primary Addressee"]);
                var softCredit = GetCellText(ws, rowNumber, map["Soft Credit Recipient Name"]);
                var sourceName = GetCellText(ws, rowNumber, map["Name"]);
                var identity = _identityResolver.Resolve(new DonorIdentityInput(fund ?? string.Empty, sourceName, softCredit, addressee, isAnonymous));

                var parsedRow = new DonationImportRowDto
                {
                    SourceRowNumber = rowNumber,
                    Date = date,
                    AccountName = sourceName ?? string.Empty,
                    PaymentMethod = GetCellText(ws, rowNumber, map["Gift Payment Type"]) ?? string.Empty,
                    GiftType = GetCellText(ws, rowNumber, map["Gift Type"]) ?? string.Empty,
                    Amount = amount,
                    Fund = fund ?? string.Empty,
                    GiftImportId = GetCellText(ws, rowNumber, map["Gift Import ID"]),
                    HonorMemorialName = GetCellText(ws, rowNumber, honorMemorialColumn.Value),
                    PrimaryAddressee = isAnonymous ? null : addressee,
                    SoftCreditName = isAnonymous ? null : softCredit,
                    Address = isAnonymous ? null : GetCellText(ws, rowNumber, map["Preferred Address Line 1"]),
                    City = isAnonymous ? null : GetCellText(ws, rowNumber, map["Preferred City"]),
                    State = isAnonymous ? null : GetCellText(ws, rowNumber, map["Preferred State"]),
                    PostalCode = isAnonymous ? null : GetCellText(ws, rowNumber, map["Preferred ZIP"]),
                    Country = isAnonymous ? null : GetCellText(ws, rowNumber, map["Preferred Country"]),
                    Email = isAnonymous ? null : GetCellText(ws, rowNumber, map["Personal Email Number"]),
                    PhoneFixed = isAnonymous ? null : GetCellText(ws, rowNumber, map["Home Phone Number"]),
                    PhoneMobile = isAnonymous ? null : GetCellText(ws, rowNumber, map["Personal Mobile Phone Number"]),
                    IsAnonymous = isAnonymous,
                    ResolvedDonorName = identity.DisplayName,
                    DonorIdentityKey = identity.IdentityKey,
                    DonorResolutionSource = identity.ResolutionSource,
                    DonorResolutionVersion = identity.ResolutionVersion
                };
                AddObservedContacts(parsedRow);
                allRows.Add(parsedRow);

                earliest = !earliest.HasValue || date < earliest.Value ? date : earliest;
                result.TotalRows++;
            }

            var canonicalRows = ConsolidateDuplicates(allRows, result);
            _progressService.CompleteStep(progressId, "Data Analysis", "Completed", $"{canonicalRows.Count:N0} canonical rows; {result.DuplicateRowsMerged:N0} duplicates merged");

            _progressService.AddStep(progressId, "Frequency Pre-load", "Fetching prior donor history for frequency classification...");
            var donorKeys = canonicalRows
                .Where(row => !string.IsNullOrWhiteSpace(row.ResolvedDonorName) && !string.IsNullOrWhiteSpace(row.Fund))
                .Select(row => (IdentityKey: row.DonorIdentityKey!, row.Fund))
                .Distinct()
                .ToList();
            var priorHistory = earliest.HasValue
                ? await _donationRepository.GetRecentDonationsForDonorsAsync(donorKeys, earliest.Value, 30, ct)
                : new List<Cya2.Core.ReadModels.DonationRecord>();
            _progressService.CompleteStep(progressId, "Frequency Pre-load", "Completed", $"Loaded prior history for {priorHistory.Select(h => h.AccountName).Distinct().Count()} donors");

            if (earliest.HasValue)
            {
                _progressService.AddStep(progressId, "Database Backup", "Backing up and clearing the overlapping donation range...");
                _logger.LogInformation("Donation import backup starting. ProgressId={ProgressId}, SourceRangeStart={SourceRangeStart:O}", progressId, earliest.Value);
                await _donationRepository.BackupAndDeleteFromDateAsync(earliest.Value, progressId, ct);
                _progressService.CompleteStep(progressId, "Database Backup", "Completed", "Canonical donation range cleared");
                _logger.LogInformation("Donation import backup completed. ProgressId={ProgressId}, SourceRangeStart={SourceRangeStart:O}", progressId, earliest.Value);
            }

            _logger.LogInformation("Donation import frequency classification starting. ProgressId={ProgressId}, CanonicalRows={CanonicalRows}, PriorHistory={PriorHistory}", progressId, canonicalRows.Count, priorHistory.Count);
            ClassifyFrequencies(canonicalRows, priorHistory);
            _logger.LogInformation("Donation import frequency classification completed. ProgressId={ProgressId}, CanonicalRows={CanonicalRows}", progressId, canonicalRows.Count);

            _progressService.AddStep(progressId, "Data Import", "Importing canonical donation records and contact values...");
            _progressService.SetExpected(progressId, canonicalRows.Count);
            _progressService.Report(progressId, 0, 0, result.FailedRows, "Starting import...");

            var persistenceRows = canonicalRows.Select(ToPersistenceModel).ToList();
            _logger.LogInformation("Donation import persistence preparation completed. ProgressId={ProgressId}, PersistenceRows={PersistenceRows}", progressId, persistenceRows.Count);
            if (!ct.IsCancellationRequested)
            {
                _logger.LogInformation("Donation import set-based persistence starting. ProgressId={ProgressId}, RowCount={RowCount}", progressId, persistenceRows.Count);
                var persistenceResult = await _donationRepository.InsertCanonicalDonationsAsync(persistenceRows, ct);
                result.InsertedRows = persistenceResult.Inserted;
                _progressService.Report(progressId, persistenceRows.Count, result.InsertedRows, result.FailedRows, "Importing data...");
                _logger.LogInformation("Donation import set-based persistence completed. ProgressId={ProgressId}, RowCount={RowCount}, Inserted={Inserted}, ContactsAdded={ContactsAdded}", progressId, persistenceRows.Count, persistenceResult.Inserted, persistenceResult.ContactsAdded);
            }

            _progressService.CompleteStep(progressId, "Data Import", "Completed", $"{result.InsertedRows:N0} records imported, {result.FailedRows:N0} failed");
            _progressService.AddErrors(progressId, result.Errors);
            _progressService.Complete(progressId);
            _cacheInvalidator.InvalidateAll();
            _logger.LogInformation("Donation import complete. Total={Total}, Canonical={Canonical}, Inserted={Inserted}, DuplicatesMerged={DuplicatesMerged}, Failed={Failed}", result.TotalRows, canonicalRows.Count, result.InsertedRows, result.DuplicateRowsMerged, result.FailedRows);
            return result;
        }

        private static List<DonationImportRowDto> ConsolidateDuplicates(List<DonationImportRowDto> rows, ImportResult result)
        {
            var canonical = new List<DonationImportRowDto>();
            var grouped = new Dictionary<DonationDuplicateKey, DonationImportRowDto>();
            var duplicateRows = new Dictionary<DonationImportRowDto, List<int>>();

            foreach (var row in rows)
            {
                var candidate = new DonationDuplicateCandidate(row.GiftImportId, row.Fund, row.Date, Convert.ToDecimal(row.Amount), row.PaymentMethod, row.GiftType, row.DonorIdentityKey ?? string.Empty);
                if (!DonationDuplicateClassifier.TryCreateKey(candidate, out var key))
                {
                    canonical.Add(row);
                    continue;
                }

                if (!grouped.TryGetValue(key, out var existing))
                {
                    grouped[key] = row;
                    canonical.Add(row);
                    continue;
                }

                MergeContactValues(existing, row);
                result.DuplicateRowsMerged++;
                if (!duplicateRows.TryGetValue(existing, out var sourceRows))
                {
                    sourceRows = new List<int>();
                    duplicateRows[existing] = sourceRows;
                }
                sourceRows.Add(row.SourceRowNumber);
            }

            foreach (var pair in duplicateRows)
            {
                result.DuplicateReport.Add(new DonationImportReportEntry
                {
                    CanonicalRowNumber = pair.Key.SourceRowNumber,
                    DuplicateRowNumbers = pair.Value,
                    GiftImportId = pair.Key.GiftImportId ?? string.Empty,
                    Fund = pair.Key.Fund,
                    Amount = Convert.ToDecimal(pair.Key.Amount),
                    DonorName = pair.Key.ResolvedDonorName ?? pair.Key.AccountName,
                    Reason = "Rows matched on gift, fund allocation, date, amount, payment type, gift type, and resolved donor identity; contact values were merged."
                });
            }

            return canonical;
        }

        private static void MergeContactValues(DonationImportRowDto target, DonationImportRowDto source)
        {
            AddObservedContacts(source, target);
            target.Email = MergeValue(target.Email, source.Email);
            target.PhoneFixed = MergeValue(target.PhoneFixed, source.PhoneFixed);
            target.PhoneMobile = MergeValue(target.PhoneMobile, source.PhoneMobile);
            target.Address = MergeValue(target.Address, source.Address);
            target.City = MergeValue(target.City, source.City);
            target.State = MergeValue(target.State, source.State);
            target.PostalCode = MergeValue(target.PostalCode, source.PostalCode);
            target.Country = MergeValue(target.Country, source.Country);
        }

        private static void AddObservedContacts(DonationImportRowDto row, DonationImportRowDto? target = null)
        {
            target ??= row;
            AddObservedContact(target, "Email", row.Email);
            AddObservedContact(target, "HomePhone", row.PhoneFixed);
            AddObservedContact(target, "MobilePhone", row.PhoneMobile);
            AddObservedContact(target, "AddressLine1", row.Address);
            AddObservedContact(target, "City", row.City);
            AddObservedContact(target, "State", row.State);
            AddObservedContact(target, "PostalCode", row.PostalCode);
            AddObservedContact(target, "Country", row.Country);
        }

        private static void AddObservedContact(DonationImportRowDto row, string contactType, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!row.ObservedContacts.TryGetValue(contactType, out var values))
            {
                values = new List<string>();
                row.ObservedContacts[contactType] = values;
            }

            if (!values.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value.Trim());
            }
        }

        private static string? MergeValue(string? first, string? second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first;
        }

        private void ClassifyFrequencies(List<DonationImportRowDto> rows, List<Cya2.Core.ReadModels.DonationRecord> priorHistory)
        {
            var priorByDonor = priorHistory
                .GroupBy(history => (history.AccountName.ToLowerInvariant(), history.Fund.ToLowerInvariant()))
                .ToDictionary(group => group.Key, group => group.OrderBy(item => item.Date).Select(item => new DonorGiftRecord { Date = item.Date, Amount = Convert.ToDecimal(item.Amount) }).ToList());

            foreach (var group in rows.GroupBy(row => (row.ResolvedDonorName?.ToLowerInvariant() ?? string.Empty, row.Fund.ToLowerInvariant())))
            {
                priorByDonor.TryGetValue(group.Key, out var prior);
                prior ??= new List<DonorGiftRecord>();
                var fileGifts = group.OrderBy(row => row.Date).ToList();
                var allGifts = prior.Concat(fileGifts.Select(row => new DonorGiftRecord { Date = row.Date, Amount = Convert.ToDecimal(row.Amount) })).OrderBy(gift => gift.Date).ToList();
                foreach (var row in fileGifts)
                {
                    row.Frequency = _frequencyService.ClassifyGift(new DonorGiftRecord { Date = row.Date, Amount = Convert.ToDecimal(row.Amount) }, allGifts).Frequency;
                }
            }
        }

        private static CanonicalDonationWriteModel ToPersistenceModel(DonationImportRowDto row)
        {
            var donor = new DonorWriteModel
            {
                Fund = row.Fund,
                DisplayName = row.ResolvedDonorName ?? row.AccountName,
                IdentityKey = row.DonorIdentityKey ?? DonorIdentityResolver.NormalizeIdentityKey(row.AccountName),
                ResolutionSource = row.DonorResolutionSource ?? "AccountName",
                ResolutionVersion = row.DonorResolutionVersion
            };

            return new CanonicalDonationWriteModel
            {
                Donor = donor,
                Date = row.Date,
                GiftImportId = row.GiftImportId,
                AccountName = row.AccountName,
                PaymentMethod = row.PaymentMethod,
                GiftType = row.GiftType,
                Amount = Convert.ToDecimal(row.Amount),
                Fund = row.Fund,
                Intern = row.Intern,
                PrimaryAddressee = row.PrimaryAddressee,
                SoftCreditName = row.SoftCreditName,
                 HonorMemorialName = row.HonorMemorialName,
                IsAnonymous = row.IsAnonymous,
                Frequency = row.Frequency,
                Contacts = BuildContacts(row)
            };
        }

        private static IReadOnlyList<DonorContactWriteModel> BuildContacts(DonationImportRowDto row)
        {
            if (row.IsAnonymous)
            {
                return Array.Empty<DonorContactWriteModel>();
            }

            return row.ObservedContacts
                .SelectMany(item => item.Value.Select(value => new { Type = item.Key, Value = value }))
                .Select(item => new DonorContactWriteModel
                {
                    ContactType = item.Type,
                    ContactValue = item.Value,
                     NormalizedValue = NormalizeContact(item.Type, item.Value)
                })
                .ToList();
        }

        private static string NormalizeContact(string type, string value)
        {
            var trimmed = value.Trim();
            return type.EndsWith("Phone", StringComparison.OrdinalIgnoreCase)
                ? new string(trimmed.Where(char.IsDigit).ToArray())
                : DonorIdentityResolver.NormalizeWhitespace(trimmed).ToUpperInvariant();
        }

        private static Dictionary<string, int> BuildColumnMap(ExcelWorksheet ws, int headerRow)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lastColumn = ws.Dimension?.End.Column ?? 0;
            for (var column = 1; column <= lastColumn; column++)
            {
                var header = ws.Cells[headerRow, column]?.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(header) && !map.ContainsKey(header))
                {
                    map[header] = column;
                }
            }
            return map;
        }

        private static int? GetFirstAvailableColumn(
            IReadOnlyDictionary<string, int> map,
            params string[] columnNames)
        {
            foreach (var columnName in columnNames)
            {
                if (map.TryGetValue(columnName, out var column))
                {
                    return column;
                }
            }

            return null;
        }

        private static string? GetCellText(ExcelWorksheet ws, int row, int column)
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
    }
}
