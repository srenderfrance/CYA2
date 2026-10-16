using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Enums;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Core.Utilities;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Cya2.Application.Services
{
    /// <summary>
    /// Donor management service implementation backed by donation read repository.
    /// </summary>
    public class DonorService : IDonorService
    {
        private readonly IDonationReadRepository _donationReadRepository;
        private readonly IDonorContactHydrationService _donorContactHydrationService;
        private readonly ILogger<DonorService> _logger;
        private readonly IDonorSummaryCache _donorSummaryCache;
        private readonly IMissingGiftCache _missingGiftCache;
        private readonly IAccountFactDataService _accountFactDataService;
        private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
        private readonly DonorFrequencyService _frequencyService;
        private readonly DonorMissingGiftService _missingGiftService;
        private readonly DonorIdentityResolver _identityResolver;
        private string? _lastQuery;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _queryLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<string>> _accountFundsCache = new(StringComparer.OrdinalIgnoreCase);

        private long CurrentDonationsGeneration => _cacheInvalidationVersion.Donations;
        private long CurrentDonorContactsGeneration => _cacheInvalidationVersion.DonorContacts;
        private long CurrentAuthorizationGeneration => _cacheInvalidationVersion.Authorization;

        public DonorService(
            IDonationReadRepository donationReadRepository,
            IDonorContactHydrationService donorContactHydrationService,
            IAccountFactDataService accountFactDataService,
            ILogger<DonorService> logger,
            IDonorSummaryCache donorSummaryCache,
            IMissingGiftCache missingGiftCache,
            ICacheInvalidationVersion cacheInvalidationVersion,
            DonorFrequencyService frequencyService,
            DonorMissingGiftService missingGiftService,
            DonorIdentityResolver identityResolver)
        {
            _donationReadRepository = donationReadRepository;
            _donorContactHydrationService = donorContactHydrationService;
            _logger = logger;
            _donorSummaryCache = donorSummaryCache;
            _missingGiftCache = missingGiftCache;
            _accountFactDataService = accountFactDataService;
            _cacheInvalidationVersion = cacheInvalidationVersion;
            _frequencyService = frequencyService;
            _missingGiftService = missingGiftService;
            _identityResolver = identityResolver;
        }

        public async Task<List<DonorSummaryDto>> GetDonorSummariesForSelectionAsync(
            AccountOptionDto account,
            string selectedSubAccount,
            DateRange? dateRange,
            string userId = "")
        {
            var subAccounts = await GetSubAccountsForAccountAsync(account.AccountId);
            var separateFunds = subAccounts
                .Where(subAccount => subAccount.IsSeparate())
                .Select(subAccount => subAccount.SubFund)
                .Where(fund => !string.IsNullOrWhiteSpace(fund))
                .ToList();

            if (string.Equals(selectedSubAccount, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                return dateRange is null
                    ? await GetAllDonorSummariesAsync(new[] { account.Fund })
                    : await GetDonorSummariesForAccountFactsAsync(account, new[] { account.Fund }, dateRange!, userId);
            }

            if (string.Equals(selectedSubAccount, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (separateFunds.Count == 0)
                {
                    return dateRange is null
                        ? await GetAllDonorSummariesAsync(account.AccountId, account.Fund)
                        : await GetDonorSummariesForAccountAsync(account, dateRange!, userId);
                }

                var funds = new[] { account.Fund }.Concat(separateFunds);
                return dateRange is null
                    ? await GetAllDonorSummariesAsync(funds)
                    : await GetDonorSummariesForAccountFactsAsync(account, funds, dateRange!, userId);
            }

            if (selectedSubAccount.StartsWith("Sub_", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(selectedSubAccount[4..], out var subAccountId))
            {
                var selectedFund = subAccounts
                    .Where(subAccount => subAccount.IsSeparate() && subAccount.Id == subAccountId)
                    .Select(subAccount => subAccount.SubFund)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(selectedFund))
                {
                    return dateRange is null
                        ? await GetAllDonorSummariesAsync(new[] { selectedFund })
                        : await GetDonorSummariesForAccountFactsAsync(account, new[] { selectedFund }, dateRange!, userId);
                }
            }

            return dateRange is null
                ? await GetAllDonorSummariesAsync(account.AccountId, account.Fund)
                : await GetDonorSummariesForAccountAsync(account, dateRange!, userId);
        }

        public async Task<List<DonorSummaryDto>> GetMissingGiftDonorsAsync(AccountOptionDto account, DateRange dateRange)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Fund))
            {
                return new List<DonorSummaryDto>();
            }

            if (_missingGiftCache.TryGet(
                    account.AccountId,
                    account.Fund,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    _cacheInvalidationVersion.Donations,
                    _cacheInvalidationVersion.DonorContacts,
                    _cacheInvalidationVersion.Authorization,
                    out var cached))
            {
                return cached;
            }

            var summaries = await GetDonorSummariesForAccountAsync(account, dateRange);

            var missingGiftDonors = summaries
                .Where(summary => summary.HasMissingGiftAlert)
                .Select(summary => new DonorSummaryDto
                {
                    Name = summary.Name,
                    Frequency = summary.Frequency,
                    HasMissingGiftAlert = true,
                    MissingMonths = summary.MissingMonths.ToList()
                })
                .Where(summary => summary.MissingMonths.Count > 0)
                .ToList();

            _missingGiftCache.Set(
                account.AccountId,
                account.Fund,
                dateRange.StartDate,
                dateRange.EndDate,
                _cacheInvalidationVersion.Donations,
                _cacheInvalidationVersion.DonorContacts,
                _cacheInvalidationVersion.Authorization,
                missingGiftDonors);

            return missingGiftDonors;
        }

        public async Task WarmMissingGiftDonorsFromDataAsync(
            AccountOptionDto account,
            IReadOnlyList<DonationRecord> donations,
            DateRange dateRange,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(account);
            ArgumentNullException.ThrowIfNull(donations);
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(account.Fund))
            {
                return;
            }

            if (_missingGiftCache.TryGet(
                    account.AccountId,
                    account.Fund,
                    dateRange.StartDate,
                    dateRange.EndDate,
                    CurrentDonationsGeneration,
                    CurrentDonorContactsGeneration,
                    CurrentAuthorizationGeneration,
                    out _))
            {
                return;
            }

            var rangedDonations = donations
                .Where(d => d.Date.Date >= dateRange.StartDate.Date && d.Date.Date <= dateRange.EndDate.Date)
                .ToList();
            await HydrateDonorContactsAsync(rangedDonations, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var missingGiftDonors = BuildDonorSummaries(DeduplicateDonations(rangedDonations))
                .Where(summary => summary.HasMissingGiftAlert && summary.MissingMonths.Count > 0)
                .Select(summary => new DonorSummaryDto
                {
                    Name = summary.Name,
                    Frequency = summary.Frequency,
                    HasMissingGiftAlert = true,
                    MissingMonths = summary.MissingMonths.ToList()
                })
                .ToList();

            _missingGiftCache.Set(
                account.AccountId,
                account.Fund,
                dateRange.StartDate,
                dateRange.EndDate,
                CurrentDonationsGeneration,
                CurrentDonorContactsGeneration,
                CurrentAuthorizationGeneration,
                missingGiftDonors);
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
            // var sw = Stopwatch.StartNew();
            var funds = NormalizeFunds(fundNames);
            if (!funds.Any()) return new List<DonorSummaryDto>();

            var signature = BuildFundsSignature(funds);
            if (_donorSummaryCache.TryGet(signature, dateRange.StartDate, dateRange.EndDate, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, out var cached))
            {
                return cached;
            }

            var lockKey = $"range|{signature}|{dateRange.StartDate:yyyyMMdd}|{dateRange.EndDate:yyyyMMdd}";
            var gate = _queryLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_donorSummaryCache.TryGet(signature, dateRange.StartDate, dateRange.EndDate, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, out cached))
                {
                    return cached;
                }

                var donations = await _donationReadRepository.GetDonationsByFundsAndDateRangeAsync(funds, dateRange.StartDate, dateRange.EndDate);
                if (TryGetSingleInternFund(funds, out var internFund))
                {
                    donations = await GetInternDonationsByRangeAsync(internFund, dateRange.StartDate, dateRange.EndDate);
                }
                donations = DeduplicateDonations(donations);
                await HydrateDonorContactsAsync(donations);
                _lastQuery = "GetDonationsByFundsAndDateRange";

                var result = BuildDonorSummaries(donations)
                    .OrderByDescending(d => d.Total)
                    .ThenBy(d => d.Name)
                    .ToList();

                _donorSummaryCache.Set(signature, dateRange.StartDate, dateRange.EndDate, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, result);
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

        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(AccountOptionDto account, DateRange dateRange, string userId = "")
            => GetDonorSummariesForAccountFactsAsync(account, [account.Fund], dateRange, userId, includeSubAccounts: true);

        private async Task<List<DonorSummaryDto>> GetDonorSummariesForAccountFactsAsync(
            AccountOptionDto account,
            IEnumerable<string> selectedFunds,
            DateRange dateRange,
            string userId,
            bool includeSubAccounts = false)
        {
            var accountFacts = await _accountFactDataService.GetDonationDataAsync(
                new UserAccountContextAccount
                {
                    AccountId = account.AccountId,
                    Fund = account.Fund,
                    InternName = account.InternName,
                    AccountingClass = account.AccountingClass,
                    AccountNumber = account.AccountNumber,
                    Overhead = account.Overhead
                },
                userId,
                dateRange.StartDate,
                dateRange.EndDate);

            var funds = NormalizeFunds(selectedFunds);
            if (includeSubAccounts)
            {
                funds = NormalizeFunds(funds.Concat(accountFacts.SubAccounts.Select(subAccount => subAccount.SubFund)));
            }

            var signature = BuildFundsSignature(funds);
            if (_donorSummaryCache.TryGet(signature, dateRange.StartDate, dateRange.EndDate, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, out var cached))
            {
                return cached;
            }

            var lockKey = $"facts|{signature}|{dateRange.StartDate:yyyyMMdd}|{dateRange.EndDate:yyyyMMdd}";
            var gate = _queryLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (_donorSummaryCache.TryGet(signature, dateRange.StartDate, dateRange.EndDate, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, out cached))
                {
                    return cached;
                }

                var donations = string.IsNullOrWhiteSpace(account.InternName)
                    ? accountFacts.Donations.Where(donation => funds.Contains(donation.Fund, StringComparer.OrdinalIgnoreCase)).ToList()
                    : accountFacts.Donations;
                await HydrateDonorContactsAsync(donations);
                var result = BuildDonorSummaries(DeduplicateDonations(donations))
                    .OrderByDescending(donor => donor.Total)
                    .ThenBy(donor => donor.Name)
                    .ToList();
                _donorSummaryCache.Set(signature, dateRange.StartDate, dateRange.EndDate, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, result);
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
        // var sw = Stopwatch.StartNew();
            var funds = NormalizeFunds(fundNames);
            if (!funds.Any()) return new List<DonorSummaryDto>();

        var signature = BuildFundsSignature(funds);
        var allDatesStart = DateTime.MinValue.Date;
        var allDatesEnd = DateTime.MaxValue.Date;
        if (_donorSummaryCache.TryGet(signature, allDatesStart, allDatesEnd, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, out var cached))
        {
            return cached;
        }

        var allDatesLockKey = $"all|{signature}";
        var allDatesGate = _queryLocks.GetOrAdd(allDatesLockKey, _ => new SemaphoreSlim(1, 1));
        await allDatesGate.WaitAsync();
        try
        {
            if (_donorSummaryCache.TryGet(signature, allDatesStart, allDatesEnd, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, out cached))
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
            await HydrateDonorContactsAsync(donations);
            _lastQuery = "GetDonationsByFunds (all donors)";

            var result = BuildDonorSummaries(donations).OrderByDescending(d => d.Total).ThenBy(d => d.Name).ToList();
            _donorSummaryCache.Set(signature, allDatesStart, allDatesEnd, CurrentDonationsGeneration, CurrentDonorContactsGeneration, CurrentAuthorizationGeneration, result);

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

        public async Task<DonorDetailDto?> GetDonorDetailForAccountAsync(string donorName, AccountOptionDto account)
        {
            var funds = await GetExpandedFundsForAccountAsync(account.AccountId, account.Fund);

            foreach (var fund in funds)
            {
                if (string.IsNullOrWhiteSpace(fund))
                {
                    continue;
                }

                var donorNames = await GetDonorNamesAsync(fund);
                if (!donorNames.Any(name => string.Equals(name, donorName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var detail = await GetDonorDetailAsync(donorName, fund);
                if (detail != null)
                {
                    return detail;
                }
            }

            return null;
        }

        public async Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund)
        {
            var donations = await GetDonationsForFundAsync(accountFund);
            _lastQuery = "GetDonationsByFunds (filtered by donor display name)";

            var normalizedLookupName = NormalizeDonorLookupName(donorName);

            var matchingDonations = donations
                .Where(d => string.Equals(ResolveDonorDisplayName(d), normalizedLookupName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            await HydrateDonorContactsAsync(matchingDonations);

            var r = matchingDonations
                .OrderByDescending(d => d.Date)
                .FirstOrDefault();

            if (r == null) return null;

            return new DonorDetailDto
            {
                Name = ResolveDonorDisplayName(r),
                Email = JoinContactValues(matchingDonations.Select(d => d.Email)),
                PhoneMobile = JoinContactValues(matchingDonations.Select(d => d.PhoneMobile)),
                PhoneFixed = JoinContactValues(matchingDonations.Select(d => d.PhoneFixed)),
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

        private Task HydrateDonorContactsAsync(IEnumerable<DonationRecord> donations, CancellationToken cancellationToken = default)
            => _donorContactHydrationService.HydrateAsync(donations, cancellationToken);

        private async Task<List<DonationRecord>> GetInternDonationsByRangeAsync(string internFund, DateTime startDate, DateTime endDate)
        {
            if (!InternAccountUtility.TryGetInternDesignationName(internFund, out var internDesignationName))
            {
                return new List<DonationRecord>();
            }

            var donations = await _donationReadRepository.GetInternDonationsByInternNameAndDateRangeAsync(
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
                .GroupBy(
                    x => $"{x.Donation.DonorId}\u001F{x.Identity.DisplayName.Trim()}",
                    StringComparer.OrdinalIgnoreCase);

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

                var emailSummary = JoinContactValues(donorRows.Select(x => x.Email));
                var mostRecentWithAddress = donorRows.OrderByDescending(x => x.Date).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Address));

                var mobilePhones = JoinContactValues(donorRows.Select(x => x.PhoneMobile));
                var fixedPhones = JoinContactValues(donorRows.Select(x => x.PhoneFixed));
                var phones = JoinContactValues(donorRows.SelectMany(x => new[] { x.PhoneMobile, x.PhoneFixed }));

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
                var donorDisplayName = identities.First().DisplayName;

                // Detect missing gift alerts for monthly donors.
                var missingAlerts = frequency == DonorFrequency.Monthly
                    ? _missingGiftService.GetMissingGiftAlerts(donorDisplayName, giftHistory, dataFreshThrough)
                    : new List<Cya2.Core.Services.MissingGiftAlert>();

                yield return new DonorSummaryDto
                {
                    Name = donorDisplayName,
                    SourceSummary = sourceSummary,
                    Total = donorRows.Sum(x => Convert.ToDecimal(x.Amount)),
                    Email = emailSummary,
                    PhoneMobile = mobilePhones,
                    PhoneFixed = fixedPhones,
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

        private DonorIdentity ResolveDonorIdentity(DonationRecord record)
        {
            if (!record.IsAnonymous && !string.IsNullOrWhiteSpace(record.DonorDisplayName))
            {
                return new DonorIdentity
                {
                    DisplayName = record.DonorDisplayName.Trim(),
                    SourceOrganization = string.Empty,
                    IsDirect = true
                };
            }

            var resolved = _identityResolver.Resolve(new DonorIdentityInput(
                record.Fund,
                record.AccountName,
                record.SoftCreditName,
                record.Addressee,
                record.IsAnonymous));
            return new DonorIdentity
            {
                DisplayName = record.IsAnonymous ? string.Empty : resolved.DisplayName,
                SourceOrganization = string.Empty,
                IsDirect = true
            };
        }

        private string ResolveDonorDisplayName(DonationRecord record)
        {
            return record.IsAnonymous
                ? string.Empty
                : !string.IsNullOrWhiteSpace(record.DonorDisplayName)
                    ? record.DonorDisplayName.Trim()
                    : ResolveDonorIdentity(record).DisplayName;
        }

        private static string BuildDonorSummaryName(string canonicalName, bool hasDirect, IReadOnlyCollection<string> sourceOrganizations)
        {
            if (!hasDirect && sourceOrganizations.Count > 0)
            {
                return $"{canonicalName} (via {string.Join(", ", sourceOrganizations)})";
            }

            return canonicalName;
        }

        private static string JoinContactValues(IEnumerable<string?> values)
        {
            return string.Join("; ", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(value => value!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase));
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