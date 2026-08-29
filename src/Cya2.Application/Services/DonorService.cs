using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Enums;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Core.Utilities;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace Cya2.Application.Services
{
    /// <summary>
    /// Donor management service implementation backed by donation read repository.
    /// </summary>
    public class DonorService : IDonorService
    {
        private readonly IDonationReadRepository _donationReadRepository;
        private readonly IUserAccountContextService _userAccountContextService;
        private readonly ILogger<DonorService> _logger;
        private readonly ISessionDonorSummaryCacheService _donorSummaryCache;
        private readonly IAccountSnapshotCache _accountSnapshotCache;
        private readonly IAccountSnapshotLoader _accountSnapshotLoader;
        private readonly DonorFrequencyService _frequencyService;
        private readonly DonorMissingGiftService _missingGiftService;
        private string? _lastQuery;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _queryLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<string>> _accountFundsCache = new(StringComparer.OrdinalIgnoreCase);

        public DonorService(
            IDonationReadRepository donationReadRepository,
            IUserAccountContextService userAccountContextService,
            ILogger<DonorService> logger,
            ISessionDonorSummaryCacheService donorSummaryCache,
            IAccountSnapshotCache accountSnapshotCache,
            IAccountSnapshotLoader accountSnapshotLoader)
        {
            _donationReadRepository = donationReadRepository;
            _userAccountContextService = userAccountContextService;
            _logger = logger;
            _donorSummaryCache = donorSummaryCache;
            _accountSnapshotCache = accountSnapshotCache;
            _accountSnapshotLoader = accountSnapshotLoader;
            _frequencyService = new DonorFrequencyService();
            _missingGiftService = new DonorMissingGiftService();
        }

        public async Task<List<string>> GetDonorNamesAsync(string accountFund)
        {
            var donations = await GetDonationsForFundAsync(accountFund);
            _lastQuery = "GetDonationsByFunds";

            return donations
                .Select(ResolveDonorDisplayName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList()!;
        }

        public Task<List<SubAccount>> GetSubAccountsForAccountAsync(int accountId)
            => _donationReadRepository.GetSubAccountsByAccountIdAsync(accountId);

        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange)
        {
            return GetDonorSummariesAsync(new[] { accountFund }, dateRange);
        }

        public async Task<List<DonorSummaryDto>> GetDonorSummariesAsync(IEnumerable<string> fundNames, DateRange dateRange)
        {
            var sw = Stopwatch.StartNew();
            var funds = NormalizeFunds(fundNames);
            if (!funds.Any()) return new List<DonorSummaryDto>();

            var signature = BuildFundsSignature(funds);
            if (_donorSummaryCache.TryGetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, out var cached))
            {
                return cached;
            }

            var lockKey = $"range|{signature}|{dateRange.StartDate:yyyyMMdd}|{dateRange.EndDate:yyyyMMdd}";
            var gate = _queryLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_donorSummaryCache.TryGetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, out cached))
                {
                    return cached;
                }

                var donations = await _donationReadRepository.GetDonationsByFundsAndDateRangeAsync(funds, dateRange.StartDate, dateRange.EndDate);
                if (TryGetSingleInternFund(funds, out var internFund))
                {
                    donations = await GetInternDonationsByRangeAsync(internFund, dateRange.StartDate, dateRange.EndDate);
                }
                donations = DeduplicateDonations(donations);
                _lastQuery = "GetDonationsByFundsAndDateRange";

                var result = BuildDonorSummaries(donations)
                    .OrderByDescending(d => d.Total)
                    .ThenBy(d => d.Name)
                    .ToList();

                _donorSummaryCache.SetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, result);
                return result;
            }
            finally
            {
                gate.Release();
                if (gate.CurrentCount == 1)
                {
                    _queryLocks.TryRemove(lockKey, out _);
                }
            }
        }

        public async Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(int accountId, string accountFund, DateRange dateRange)
        {
            var funds = await GetExpandedFundsForAccountAsync(accountId, accountFund);
            return await GetDonorSummariesAsync(funds, dateRange);
        }

        public async Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(AccountOptionDto account, DateRange dateRange)
        {
            if (!CanUseAccountSnapshot(dateRange))
            {
                var fallbackFunds = await GetExpandedFundsForAccountAsync(account.AccountId, account.Fund);
                return await GetDonorSummariesAsync(fallbackFunds, dateRange);
            }

            var (snapshot, wasCached) = await LoadAccountSnapshotAsync(account);
            var funds = NormalizeFunds(new[] { account.Fund }.Concat(snapshot.SubAccounts.Select(subAccount => subAccount.SubFund)));
            var signature = BuildFundsSignature(funds);
            if (_donorSummaryCache.TryGetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, out var cached))
            {
                return cached;
            }

            var lockKey = $"snapshot|{signature}|{dateRange.StartDate:yyyyMMdd}|{dateRange.EndDate:yyyyMMdd}";
            var gate = _queryLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_donorSummaryCache.TryGetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, out cached))
                {
                    return cached;
                }

                _logger.LogInformation(
                    "Donor data source={Source} account={Account} requestedRange={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} queriedRange={QueriedStart:yyyy-MM-dd}..{QueriedEnd:yyyy-MM-dd} snapshotCreatedUtc={SnapshotCreatedUtc:o} donations={DonationCount}",
                    wasCached ? "snapshot-cache" : "snapshot-load",
                    account.Fund,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    GetSnapshotQueryRange().StartDate,
                    GetSnapshotQueryRange().EndDate,
                    snapshot.CreatedUtc,
                    snapshot.Donations.Count);

                var snapshotFunds = funds.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var snapshotDonations = snapshot.Donations
                    .Where(d => snapshotFunds.Contains(d.Fund))
                    .Where(d => d.Date.Date >= dateRange.StartDate.Date && d.Date.Date <= dateRange.EndDate.Date)
                    .Select(MapToDonationRecord)
                    .ToList();

                var result = BuildDonorSummaries(DeduplicateDonations(snapshotDonations))
                    .OrderByDescending(d => d.Total)
                    .ThenBy(d => d.Name)
                    .ToList();
                _donorSummaryCache.SetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, result);
                return result;
            }
            finally
            {
                gate.Release();
                if (gate.CurrentCount == 1)
                {
                    _queryLocks.TryRemove(lockKey, out _);
                }
            }
        }


        public async Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(IEnumerable<string> fundNames)
        {
        var sw = Stopwatch.StartNew();
            var funds = NormalizeFunds(fundNames);
            if (!funds.Any()) return new List<DonorSummaryDto>();

        var signature = BuildFundsSignature(funds);
        var allDatesStart = DateTime.MinValue.Date;
        var allDatesEnd = DateTime.MaxValue.Date;
        if (_donorSummaryCache.TryGetDonorSummaries(signature, allDatesStart, allDatesEnd, out var cached))
        {
            return cached;
        }

        var allDatesLockKey = $"all|{signature}";
        var allDatesGate = _queryLocks.GetOrAdd(allDatesLockKey, _ => new SemaphoreSlim(1, 1));
        await allDatesGate.WaitAsync();
        try
        {
            if (_donorSummaryCache.TryGetDonorSummaries(signature, allDatesStart, allDatesEnd, out cached))
            {
                return cached;
            }

            List<DonationRecord> donations;
            if (TryGetSingleInternFund(funds, out var internFund))
            {
                donations = await GetInternDonationsByRangeAsync(internFund, DateTime.MinValue.Date, DateTime.MaxValue.Date);
            }
            else
            {
                donations = await _donationReadRepository.GetDonationsByFundsAsync(funds);
            }
            donations = DeduplicateDonations(donations);
            _lastQuery = "GetDonationsByFunds (all donors)";

            var result = BuildDonorSummaries(donations).OrderByDescending(d => d.Total).ThenBy(d => d.Name).ToList();
            _donorSummaryCache.SetDonorSummaries(signature, allDatesStart, allDatesEnd, result);

            return result;
        }
        finally
        {
            allDatesGate.Release();
            if (allDatesGate.CurrentCount == 1)
            {
                _queryLocks.TryRemove(allDatesLockKey, out _);
            }
        }
        }

        public async Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(int accountId, string accountFund)
        {
            var funds = await GetExpandedFundsForAccountAsync(accountId, accountFund);

            return await GetAllDonorSummariesAsync(funds);
        }

        private async Task<List<string>> GetExpandedFundsForAccountAsync(int accountId, string accountFund)
        {
            var normalizedFund = string.IsNullOrWhiteSpace(accountFund) ? string.Empty : accountFund.Trim();
            var key = $"{accountId}|{normalizedFund}";
            if (_accountFundsCache.TryGetValue(key, out var cachedFunds) && cachedFunds.Count > 0)
            {
                return cachedFunds;
            }

            var subAccounts = await _donationReadRepository.GetSubAccountsByAccountIdAsync(accountId);
            var funds = new List<string> { normalizedFund };
            funds.AddRange(subAccounts.Select(s => s.SubFund));
            funds = NormalizeFunds(funds);

            _accountFundsCache[key] = funds;

            return funds;
        }

        private async Task<(AccountDataSnapshot Snapshot, bool WasCached)> LoadAccountSnapshotAsync(
            AccountOptionDto account)
        {
            var key = new AccountSnapshotKey(account.AccountId, account.Fund, 0).Normalize();
            var queryRange = GetSnapshotQueryRange();
            var wasCached = _accountSnapshotCache.TryGet(key, out var snapshot);

            if (!wasCached)
            {
                snapshot = await _accountSnapshotCache.GetOrCreateAsync(
                    key,
                    cancellationToken => LoadSnapshotAccountAsync(account, queryRange, key, cancellationToken),
                    CancellationToken.None);
            }

            return (snapshot, wasCached);
        }

        private Task<AccountDataSnapshot> LoadSnapshotAccountAsync(
            AccountOptionDto account,
            DateRange queryRange,
            AccountSnapshotKey key,
            CancellationToken cancellationToken)
        {
            return _accountSnapshotLoader.LoadAsync(
                new UserAccountContextAccount
                {
                    AccountId = account.AccountId,
                    Fund = account.Fund,
                    AccountingClass = account.AccountingClass,
                    AccountNumber = account.AccountNumber,
                    Overhead = account.Overhead
                },
                queryRange,
                key,
                cancellationToken);
        }

        private static bool CanUseAccountSnapshot(DateRange range)
        {
            var now = DateTime.UtcNow;
            return range.StartDate.Date >= new DateTime(now.Year - 2, 1, 1) &&
                   range.EndDate.Date <= new DateTime(now.Year, 12, 31);
        }

        private static DateRange GetSnapshotQueryRange()
        {
            var now = DateTime.UtcNow;
            return new DateRange(new DateTime(now.Year - 2, 1, 1), new DateTime(now.Year, 12, 31));
        }

        private static DonationRecord MapToDonationRecord(DonationSnapshot snapshot)
        {
            return new DonationRecord
            {
                Id = snapshot.Id,
                Date = snapshot.Date,
                Frequency = snapshot.Frequency,
                AccountName = snapshot.AccountName,
                PaymentMethod = snapshot.PaymentMethod,
                GiftType = snapshot.GiftType,
                Amount = snapshot.Amount,
                Fund = snapshot.Fund,
                Intern = snapshot.Intern,
                HonorMemorialName = snapshot.HonorMemorialName,
                Addressee = snapshot.Addressee,
                SoftCreditName = snapshot.SoftCreditName,
                Address = snapshot.Address,
                City = snapshot.City,
                State = snapshot.State,
                PostalCode = snapshot.PostalCode,
                Country = snapshot.Country,
                Email = snapshot.Email,
                PhoneFixed = snapshot.PhoneFixed,
                PhoneMobile = snapshot.PhoneMobile,
                DateCreated = snapshot.DateCreated,
                IsAnonymous = snapshot.IsAnonymous
            };
        }
        public async Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund)
        {
            var donations = await GetDonationsForFundAsync(accountFund);
            _lastQuery = "GetDonationsByFunds (filtered by donor display name)";

            var normalizedLookupName = NormalizeDonorLookupName(donorName);

            var matchingDonations = donations
                .Where(d => string.Equals(ResolveDonorDisplayName(d), normalizedLookupName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var r = matchingDonations
                .OrderByDescending(d => d.Date)
                .FirstOrDefault();

            if (r == null) return null;

            return new DonorDetailDto
            {
                Name = r.AccountName ?? string.Empty,
                Email = r.Email ?? string.Empty,
                PhoneMobile = r.PhoneMobile ?? string.Empty,
                PhoneFixed = r.PhoneFixed ?? string.Empty,
                Address = r.Address ?? string.Empty,
                City = r.City ?? string.Empty,
                State = r.State ?? string.Empty,
                PostalCode = r.PostalCode ?? string.Empty,
                Country = r.Country ?? string.Empty
            };
        }

        public Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund)
        {
            return Task.Run(async () =>
            {
                var d = await GetDonorDetailAsync(donorName, accountFund);
                if (d == null) return string.Empty;

                var parts = new List<string> { d.Name };
                if (!string.IsNullOrWhiteSpace(d.Email)) parts.Add(d.Email);

                var phones = new List<string>();
                if (!string.IsNullOrWhiteSpace(d.PhoneMobile)) phones.Add(d.PhoneMobile);
                if (!string.IsNullOrWhiteSpace(d.PhoneFixed)) phones.Add(d.PhoneFixed);
                if (phones.Any()) parts.Add(string.Join("; ", phones));
                if (!string.IsNullOrWhiteSpace(d.Address)) parts.Add(d.Address);

                return string.Join("\n", parts);
            });
        }

        public async Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund)
        {
            var donations = await GetDonationsForFundAsync(accountFund);
            _lastQuery = "GetDonationsByFunds (search by donor display name)";

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                donations = donations
                    .Where(d => ResolveDonorDisplayName(d).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return BuildDonorSummaries(donations)
                .OrderByDescending(d => d.Total)
                .ThenBy(d => d.Name)
                .Take(100)
                .ToList();
        }

        public Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed, string address, string city, string state, string postal, string country)
        {
            _logger.LogWarning("UpdateDonorContactInfoAsync called but not implemented");
            return Task.CompletedTask;
        }

        public string? GetLastQuery() => _lastQuery;

        private static List<string> NormalizeFunds(IEnumerable<string> fundNames)
        {
            return (fundNames ?? Enumerable.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }

        private static bool TryGetSingleInternFund(IReadOnlyCollection<string> funds, out string internFund)
        {
            internFund = string.Empty;
            if (funds.Count != 1)
            {
                return false;
            }

            var candidate = funds.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(candidate) || !InternAccountUtility.IsInternFund(candidate))
            {
                return false;
            }

            internFund = candidate;
            return true;
        }

        private async Task<List<DonationRecord>> GetDonationsForFundAsync(string accountFund)
        {
            if (InternAccountUtility.IsInternFund(accountFund))
            {
                return await GetInternDonationsByRangeAsync(accountFund, DateTime.MinValue.Date, DateTime.MaxValue.Date);
            }

            return await _donationReadRepository.GetDonationsByFundsAsync(new[] { accountFund });
        }

        private async Task<List<DonationRecord>> GetInternDonationsByRangeAsync(string internFund, DateTime startDate, DateTime endDate)
        {
            if (!InternAccountUtility.TryGetInternDesignationName(internFund, out var internDesignationName))
            {
                return new List<DonationRecord>();
            }

            var donations = await _donationReadRepository.GetInternDonationsByDesignationAndDateRangeAsync(
                internDesignationName,
                startDate,
                endDate);

            return donations ?? new List<DonationRecord>();
        }

        private static string BuildFundsSignature(IEnumerable<string> funds)
        {
            return string.Join("||", funds.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }

        private static List<DonationRecord> DeduplicateDonations(List<DonationRecord> donations)
        {
            if (donations == null || donations.Count == 0)
            {
                return new List<DonationRecord>();
            }

            return donations
                .GroupBy(d => d.Id)
                .Select(g => g.First())
                .ToList();
        }

        private IEnumerable<DonorSummaryDto> BuildDonorSummaries(List<DonationRecord> donations)
        {
            var groups = donations
                .Select(d => new { Donation = d, Identity = ResolveDonorIdentity(d) })
                .Where(x => !string.IsNullOrWhiteSpace(x.Identity.DisplayName))
                .GroupBy(x => x.Identity.DisplayName, StringComparer.OrdinalIgnoreCase);

            // Use the latest available donation date in the loaded dataset as the
            // freshness anchor for missing-gift checks. This prevents false
            // "missing" alerts when the database is not current for the next month.
            // Example: if data only goes through April, do not mark April as missing
            // just because there are no May rows yet.
            var dataFreshThrough = donations.Any()
                ? donations.Max(d => d.Date).Date
                : DateTime.Today;

            foreach (var g in groups)
            {
                var donorRows = g.Select(x => x.Donation).ToList();
                var identities = g.Select(x => x.Identity).ToList();

                var mostRecentWithEmail = donorRows.OrderByDescending(x => x.Date).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Email));
                var mostRecentWithAddress = donorRows.OrderByDescending(x => x.Date).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Address));

                var phones = donorRows
                    .Select(x => !string.IsNullOrWhiteSpace(x.PhoneMobile) ? x.PhoneMobile : x.PhoneFixed)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                // Build gift records for frequency classification.
                var giftHistory = donorRows
                    .Select(x => new DonorGiftRecord { Date = x.Date, Amount = Convert.ToDecimal(x.Amount) })
                    .OrderBy(x => x.Date)
                    .ToList();

                // Use stored frequency from most recent donation if available,
                // otherwise fall back to live classification.
                var mostRecent = donorRows.OrderByDescending(x => x.Date).First();
                var frequency = mostRecent.Frequency
                    ?? _frequencyService.ClassifyDonor(giftHistory);

                var hasDirect = identities.Any(i => i.IsDirect);
                var sourceOrganizations = identities
                    .Select(i => i.SourceOrganization)
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var sourceSummary = BuildSourceSummary(hasDirect, sourceOrganizations);
                var donorDisplayName = BuildDonorSummaryName(g.Key, hasDirect, sourceOrganizations);

                // Detect missing gift alerts for monthly donors.
                var missingAlerts = frequency == DonorFrequency.Monthly
                    ? _missingGiftService.GetMissingGiftAlerts(g.Key, giftHistory, dataFreshThrough)
                    : new List<Cya2.Core.Services.MissingGiftAlert>();

                yield return new DonorSummaryDto
                {
                    Name = donorDisplayName,
                    SourceSummary = sourceSummary,
                    Total = donorRows.Sum(x => Convert.ToDecimal(x.Amount)),
                    Email = mostRecentWithEmail?.Email ?? string.Empty,
                    PhoneMobile = mostRecent?.PhoneMobile ?? string.Empty,
                    PhoneFixed = mostRecent?.PhoneFixed ?? string.Empty,
                    PhoneSummary = string.Join("; ", phones),
                    AddressSummary = mostRecentWithAddress?.Address ?? string.Empty,
                    City = mostRecentWithAddress?.City ?? string.Empty,
                    State = mostRecentWithAddress?.State ?? string.Empty,
                    PostalCode = mostRecentWithAddress?.PostalCode ?? string.Empty,
                    Country = mostRecentWithAddress?.Country ?? string.Empty,
                    Frequency = frequency,
                    HasMissingGiftAlert = missingAlerts.Count > 0,
                    MissingMonths = missingAlerts.Select(a => a.ExpectedMonthLabel).ToList()
                };
            }
        }

        private static DonorIdentity ResolveDonorIdentity(DonationRecord record)
        {
            var accountName = NormalizeNameValue(record.AccountName);
            var addressee = NormalizeNameValue(record.Addressee);
            var softCreditName = NormalizeNameValue(record.SoftCreditName);

            if (!string.IsNullOrWhiteSpace(softCreditName) &&
                !ContainsName(accountName, softCreditName) &&
                !ContainsName(addressee, softCreditName))
            {
                return new DonorIdentity
                {
                    DisplayName = softCreditName,
                    SourceOrganization = accountName,
                    IsDirect = false
                };
            }

            if (!string.IsNullOrWhiteSpace(addressee))
            {
                return new DonorIdentity
                {
                    DisplayName = addressee,
                    SourceOrganization = string.Empty,
                    IsDirect = true
                };
            }

            if (!string.IsNullOrWhiteSpace(accountName))
            {
                return new DonorIdentity
                {
                    DisplayName = accountName,
                    SourceOrganization = string.Empty,
                    IsDirect = true
                };
            }

            return new DonorIdentity
            {
                DisplayName = record.IsAnonymous ? string.Empty : "Unknown",
                SourceOrganization = string.Empty,
                IsDirect = true
            };
        }

        private static string ResolveDonorDisplayName(DonationRecord record)
        {
            return ResolveDonorIdentity(record).DisplayName;
        }

        private static string BuildDonorSummaryName(string canonicalName, bool hasDirect, IReadOnlyCollection<string> sourceOrganizations)
        {
            if (!hasDirect && sourceOrganizations.Count > 0)
            {
                return $"{canonicalName} (via {string.Join(", ", sourceOrganizations)})";
            }

            return canonicalName;
        }

        private static string BuildSourceSummary(bool hasDirect, IReadOnlyCollection<string> sourceOrganizations)
        {
            if (hasDirect && sourceOrganizations.Count > 0)
            {
                return $"Also gives via {string.Join(", ", sourceOrganizations)}";
            }

            return string.Empty;
        }

        private static string NormalizeDonorLookupName(string donorName)
        {
            if (string.IsNullOrWhiteSpace(donorName))
            {
                return string.Empty;
            }

            var normalized = donorName.Trim();
            var viaMarkerIndex = normalized.IndexOf(" (via ", StringComparison.OrdinalIgnoreCase);
            if (viaMarkerIndex > 0 && normalized.EndsWith(')'))
            {
                return normalized[..viaMarkerIndex].Trim();
            }

            return normalized;
        }

        private static string NormalizeNameValue(string? value)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (string.Equals(normalized, "NA", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalized;
        }

        private static bool ContainsName(string source, string candidate)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            return source.Contains(candidate, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class DonorIdentity
        {
            public string DisplayName { get; init; } = string.Empty;
            public string SourceOrganization { get; init; } = string.Empty;
            public bool IsDirect { get; init; }
        }
    }
}