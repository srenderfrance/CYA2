using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Enums;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
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
        private readonly DonorFrequencyService _frequencyService;
        private readonly DonorMissingGiftService _missingGiftService;
        private string? _lastQuery;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _queryLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<string>> _accountFundsCache = new(StringComparer.OrdinalIgnoreCase);

        public DonorService(
            IDonationReadRepository donationReadRepository,
            IUserAccountContextService userAccountContextService,
            ILogger<DonorService> logger,
            ISessionDonorSummaryCacheService donorSummaryCache)
        {
            _donationReadRepository = donationReadRepository;
            _userAccountContextService = userAccountContextService;
            _logger = logger;
            _donorSummaryCache = donorSummaryCache;
            _frequencyService = new DonorFrequencyService();
            _missingGiftService = new DonorMissingGiftService();
        }

        public async Task<List<string>> GetDonorNamesAsync(string accountFund)
        {
            var donations = await _donationReadRepository.GetDonationsByFundsAsync(new[] { accountFund });
            _lastQuery = "GetDonationsByFunds";

            return donations
                .Select(d => d.AccountName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList()!;
        }

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
                _logger.LogInformation(
                    "Donor summaries source=cache funds={FundCount} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} rows={RowCount} elapsedMs={ElapsedMs}",
                    funds.Count,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    cached.Count,
                    sw.ElapsedMilliseconds);
                return cached;
            }

            var lockKey = $"range|{signature}|{dateRange.StartDate:yyyyMMdd}|{dateRange.EndDate:yyyyMMdd}";
            var gate = _queryLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_donorSummaryCache.TryGetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, out cached))
                {
                    _logger.LogInformation(
                        "Donor summaries source=cache-after-wait funds={FundCount} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} rows={RowCount} elapsedMs={ElapsedMs}",
                        funds.Count,
                        dateRange.StartDate,
                        dateRange.EndDate,
                        cached.Count,
                        sw.ElapsedMilliseconds);
                    return cached;
                }

                var donations = await _donationReadRepository.GetDonationsByFundsAndDateRangeAsync(funds, dateRange.StartDate, dateRange.EndDate);
                donations = DeduplicateDonations(donations);
                _lastQuery = "GetDonationsByFundsAndDateRange";

                var result = BuildDonorSummaries(donations)
                    .OrderByDescending(d => d.Total)
                    .ThenBy(d => d.Name)
                    .ToList();

                _donorSummaryCache.SetDonorSummaries(signature, dateRange.StartDate, dateRange.EndDate, result);

                _logger.LogInformation(
                    "Donor summaries source=db funds={FundCount} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd} donations={DonationCount} rows={RowCount} elapsedMs={ElapsedMs}",
                    funds.Count,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    donations?.Count ?? 0,
                    result.Count,
                    sw.ElapsedMilliseconds);
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

            _logger.LogInformation(
                "Donor summaries account expansion accountId={AccountId} fund='{Fund}' subAccountCount={SubAccountCount} expandedFundCount={ExpandedFundCount} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}",
                accountId,
                accountFund,
                Math.Max(0, funds.Count - 1),
                funds.Count,
                dateRange.StartDate,
                dateRange.EndDate);

            return await GetDonorSummariesAsync(funds, dateRange);
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
            _logger.LogInformation(
                "Donor summaries source=cache-all-dates funds={FundCount} rows={RowCount} elapsedMs={ElapsedMs}",
                funds.Count,
                cached.Count,
                sw.ElapsedMilliseconds);
            return cached;
        }

        var allDatesLockKey = $"all|{signature}";
        var allDatesGate = _queryLocks.GetOrAdd(allDatesLockKey, _ => new SemaphoreSlim(1, 1));
        await allDatesGate.WaitAsync();
        try
        {
            if (_donorSummaryCache.TryGetDonorSummaries(signature, allDatesStart, allDatesEnd, out cached))
            {
                _logger.LogInformation(
                    "Donor summaries source=cache-all-dates-after-wait funds={FundCount} rows={RowCount} elapsedMs={ElapsedMs}",
                    funds.Count,
                    cached.Count,
                    sw.ElapsedMilliseconds);
                return cached;
            }

            var donations = await _donationReadRepository.GetDonationsByFundsAsync(funds);
            donations = DeduplicateDonations(donations);
            _lastQuery = "GetDonationsByFunds (all donors)";

            var result = BuildDonorSummaries(donations).OrderByDescending(d => d.Total).ThenBy(d => d.Name).ToList();
            _donorSummaryCache.SetDonorSummaries(signature, allDatesStart, allDatesEnd, result);

            _logger.LogInformation(
                "Donor summaries source=db-all-dates funds={FundCount} donations={DonationCount} rows={RowCount} elapsedMs={ElapsedMs}",
                funds.Count,
                donations?.Count ?? 0,
                result.Count,
                sw.ElapsedMilliseconds);

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

            _logger.LogInformation(
                "Donor summaries all-dates account expansion accountId={AccountId} fund='{Fund}' subAccountCount={SubAccountCount} expandedFundCount={ExpandedFundCount}",
                accountId,
                accountFund,
                Math.Max(0, funds.Count - 1),
                funds.Count);

            return await GetAllDonorSummariesAsync(funds);
        }

        private async Task<List<string>> GetExpandedFundsForAccountAsync(int accountId, string accountFund)
        {
            var normalizedFund = string.IsNullOrWhiteSpace(accountFund) ? string.Empty : accountFund.Trim();
            var key = $"{accountId}|{normalizedFund}";
            if (_accountFundsCache.TryGetValue(key, out var cachedFunds) && cachedFunds.Count > 0)
            {
                _logger.LogInformation(
                    "Donor account funds source=cache accountId={AccountId} fund='{Fund}' expandedFundCount={ExpandedFundCount}",
                    accountId,
                    normalizedFund,
                    cachedFunds.Count);
                return cachedFunds;
            }

            var subAccounts = await _donationReadRepository.GetSubAccountsByAccountIdAsync(accountId);
            var funds = new List<string> { normalizedFund };
            funds.AddRange(subAccounts.Select(s => s.SubFund));
            funds = NormalizeFunds(funds);

            _accountFundsCache[key] = funds;

            _logger.LogInformation(
                "Donor account funds source=db accountId={AccountId} fund='{Fund}' subAccountCount={SubAccountCount} expandedFundCount={ExpandedFundCount}",
                accountId,
                normalizedFund,
                subAccounts?.Count ?? 0,
                funds.Count);

            return funds;
        }
        public async Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund)
        {
            var donations = await _donationReadRepository.GetDonationsByFundsAndDonorAsync(new[] { accountFund }, donorName);
            _lastQuery = "GetDonationsByFundsAndDonor";

            var r = donations
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
            var donations = await _donationReadRepository.SearchDonationsByFundsAndDonorAsync(new[] { accountFund }, searchTerm);
            _lastQuery = "SearchDonationsByFundsAndDonor";

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
                .Where(d => !string.IsNullOrWhiteSpace(d.AccountName))
                .GroupBy(d => d.AccountName!, StringComparer.OrdinalIgnoreCase);

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
                var mostRecentWithEmail = g.OrderByDescending(x => x.Date).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Email));
                var mostRecentWithAddress = g.OrderByDescending(x => x.Date).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Address));

                var phones = g
                    .Select(x => !string.IsNullOrWhiteSpace(x.PhoneMobile) ? x.PhoneMobile : x.PhoneFixed)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                // Build gift records for frequency classification.
                var giftHistory = g
                    .Select(x => new DonorGiftRecord { Date = x.Date, Amount = Convert.ToDecimal(x.Amount) })
                    .OrderBy(x => x.Date)
                    .ToList();

                // Use stored frequency from most recent donation if available,
                // otherwise fall back to live classification.
                var mostRecent = g.OrderByDescending(x => x.Date).First();
                var frequency = mostRecent.Frequency
                    ?? _frequencyService.ClassifyDonor(giftHistory);

                // Detect missing gift alerts for monthly donors.
                var missingAlerts = frequency == DonorFrequency.Monthly
                    ? _missingGiftService.GetMissingGiftAlerts(g.Key, giftHistory, dataFreshThrough)
                    : new List<Cya2.Core.Services.MissingGiftAlert>();

                yield return new DonorSummaryDto
                {
                    Name = g.Key,
                    Total = g.Sum(x => Convert.ToDecimal(x.Amount)),
                    Email = mostRecentWithEmail?.Email ?? string.Empty,
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
    }
}