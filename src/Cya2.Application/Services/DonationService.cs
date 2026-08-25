using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.ValueObjects;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Cya2.Application.Services;

public class DonationService : IDonationService
{
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly ISessionDonationDataCacheService _donationCache;
    private readonly IAccountSnapshotCache _accountSnapshotCache;
    private readonly IAccountSnapshotLoader _accountSnapshotLoader;
    private readonly ILogger<DonationService> _logger;

    public DonationService(
        IUserAccountContextService userAccountContextService,
        IDonationReadRepository donationReadRepository,
        ISessionDonationDataCacheService donationCache,
        IAccountSnapshotCache accountSnapshotCache,
        IAccountSnapshotLoader accountSnapshotLoader,
        ILogger<DonationService> logger)
    {
        _userAccountContextService = userAccountContextService;
        _donationReadRepository = donationReadRepository;
        _donationCache = donationCache;
        _accountSnapshotCache = accountSnapshotCache;
        _accountSnapshotLoader = accountSnapshotLoader;
        _logger = logger;
    }

    public async Task<DonationDataDto> GetDonationDataAsync(string accountName, string? subAccountSelection, DateRange dateRange, string userId, bool isAdminOrViewer = false, bool forceRefresh = false)
    {
        var sw = Stopwatch.StartNew();
        var result = new DonationDataDto();
        var normalizedSubSelection = string.IsNullOrWhiteSpace(subAccountSelection) ? "All" : subAccountSelection;
        var bypassSubAccountCache = !string.Equals(normalizedSubSelection, "All", StringComparison.OrdinalIgnoreCase);
        var requestedInternAccount = InternAccountUtility.IsInternFund(accountName);
        var cacheQueryRange = GetSessionCacheQueryRange(dateRange);

        try
        {
            if (!forceRefresh && !bypassSubAccountCache && !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(accountName) && _donationCache != null &&
                !IsCanonicalSnapshotRange(cacheQueryRange))
            {
                if (_donationCache.TryGetDonationData(userId, accountName, out var directCached) &&
                    CacheCoversRequestedRange(directCached, dateRange))
                {
                    if (requestedInternAccount && (directCached?.Donations?.Count ?? 0) == 0)
                    {
                        _logger.LogInformation(
                            "Donation data cache-direct stale-for-intern user='{UserId}' selectedAccount='{SelectedAccount}' rows=0 range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}; refreshing from DB",
                            userId,
                            accountName,
                            dateRange.StartDate,
                            dateRange.EndDate);
                    }
                    else
                    {
                    _logger.LogInformation(
                        "Donation data source=cache-direct user='{UserId}' selectedAccount='{SelectedAccount}' rows={RowCount} range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd} elapsedMs={ElapsedMs}",
                        userId,
                        directCached.SelectedAccount,
                        directCached.Donations?.Count ?? 0,
                        dateRange.StartDate,
                        dateRange.EndDate,
                        sw.ElapsedMilliseconds);
                    return directCached;
                    }
                }

                if (directCached != null)
                {
                    _logger.LogInformation(
                        "Donation data cache miss user='{UserId}' account='{Account}' reason=date-range-mismatch requested={RequestedStart:yyyy-MM-dd}..{RequestedEnd:yyyy-MM-dd} cached={CachedStart:yyyy-MM-dd}..{CachedEnd:yyyy-MM-dd} bypassSubAccountCache={BypassSubAccountCache} forceRefresh={ForceRefresh}",
                        userId,
                        accountName,
                        dateRange.StartDate,
                        dateRange.EndDate,
                        directCached.CachedStartDate,
                        directCached.CachedEndDate,
                        bypassSubAccountCache,
                        forceRefresh);
                }
            }

            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            if (context == null || context.Accounts == null || context.Accounts.Count == 0)
            {
                _logger.LogWarning("Donation context unavailable for user '{UserId}' (isAdminOrViewer={IsAdminOrViewer})", userId, isAdminOrViewer);
                return result; // no accounts available
            }

            // Map available accounts
            result.UserAccounts = context.Accounts.Select(a => new AccountOptionDto
            {
                AccountId = a.AccountId,
                Fund = a.Fund,
                AccountingClass = a.AccountingClass,
                AccountNumber = a.AccountNumber,
                Overhead = a.Overhead
            }).ToList();

            // Resolve selected account preference using context helper
            var selected = _userAccountContextService.ResolveSelectedAccount(context, string.IsNullOrWhiteSpace(accountName) ? null : accountName);
            if (selected != null)
            {
                result.SelectedAccount = selected.Fund;
            }
            else if (!string.IsNullOrWhiteSpace(accountName) && result.UserAccounts.Any(u => string.Equals(u.Fund, accountName, StringComparison.OrdinalIgnoreCase)))
            {
                result.SelectedAccount = accountName;
            }
            else
            {
                // fallback to first available account for UI only
                result.SelectedAccount = result.UserAccounts.First().Fund;
            }

            result.SelectedSubAccount = normalizedSubSelection;
            var selectedIsInternAccount = selected != null && InternAccountUtility.IsInternFund(selected.Fund);
            var queryRange = bypassSubAccountCache ? dateRange : cacheQueryRange;
            AccountDataSnapshot? accountSnapshot = null;
            var donationDataSource = "repository";

            if (selected != null && IsCanonicalSnapshotRange(queryRange))
            {
                var snapshotKey = new AccountSnapshotKey(selected.AccountId, selected.Fund, 0).Normalize();
                var snapshotWasCached = _accountSnapshotCache.TryGet(snapshotKey, out var cachedSnapshot);
                accountSnapshot = snapshotWasCached
                    ? cachedSnapshot
                    : await _accountSnapshotCache.GetOrCreateAsync(
                        snapshotKey,
                        cancellationToken => _accountSnapshotLoader.LoadAsync(selected, queryRange, snapshotKey, cancellationToken),
                        CancellationToken.None);
                donationDataSource = snapshotWasCached ? "snapshot-cache" : "snapshot-load";
            }

            // Build sub-account options for the selected account when Separate sub-accounts exist.
            var separateSubAccounts = new List<Cya2.Core.Entities.SubAccount>();
            if (selected != null && !selectedIsInternAccount)
            {
                var allSubAccounts = accountSnapshot?.SubAccounts
                    .Select(sa => new Cya2.Core.Entities.SubAccount
                    {
                        Id = sa.Id,
                        AccountId = sa.AccountId,
                        SubFund = sa.SubFund,
                        Kind = sa.Kind
                    })
                    .ToList()
                    ?? await _donationReadRepository.GetSubAccountsByAccountIdAsync(selected.AccountId);
                separateSubAccounts = allSubAccounts
                    .Where(sa => string.Equals(sa.Kind, "Separate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sa.SubFund))
                    .ToList();

                result.ShowSubAccountDropdown = separateSubAccounts.Any();
                result.SubAccountOptions = new List<SubAccountOptionDto>();

                if (result.ShowSubAccountDropdown)
                {
                    result.SubAccountOptions.Add(new SubAccountOptionDto { Value = "All", DisplayText = "All", IsAll = true });
                    result.SubAccountOptions.Add(new SubAccountOptionDto
                    {
                        Value = "Primary",
                        DisplayText = selected.Fund,
                        IsPrimary = true,
                        SubFund = selected.Fund
                    });

                    foreach (var sa in separateSubAccounts)
                    {
                        result.SubAccountOptions.Add(new SubAccountOptionDto
                        {
                            Value = $"Sub_{sa.Id}",
                            DisplayText = sa.SubFund,
                            SubAccountId = sa.Id,
                            SubFund = sa.SubFund
                        });
                    }

                    // If caller passed an invalid sub selection, normalize to All.
                    if (!result.SubAccountOptions.Any(o => string.Equals(o.Value, result.SelectedSubAccount, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.SelectedSubAccount = "All";
                    }
                }
                else
                {
                    result.SelectedSubAccount = "All";
                    result.SubAccountOptions = new List<SubAccountOptionDto>();
                }
            }
            else
            {
                result.ShowSubAccountDropdown = false;
                result.SubAccountOptions = new List<SubAccountOptionDto>();
                result.SelectedSubAccount = "All";
            }

            // Determine which funds to query. If caller passed an explicit accountName use that; otherwise use user's accounts
            var fundsToQuery = new List<string>();
            if (!string.IsNullOrWhiteSpace(accountName))
            {
                fundsToQuery.Add(accountName);
            }
            else
            {
                fundsToQuery.AddRange(result.UserAccounts.Select(u => u.Fund));
            }

            // Ensure distinct and non-empty
            fundsToQuery = fundsToQuery.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // If we have a cache and caller requested a single fund, try cache first (this covers initial load where Home may have pre-cached)
            if (!forceRefresh && !bypassSubAccountCache && !string.IsNullOrWhiteSpace(result.SelectedAccount) && _donationCache != null &&
                accountSnapshot == null)
            {
                if (_donationCache.TryGetDonationData(userId, result.SelectedAccount, out var cached) &&
                    CacheCoversRequestedRange(cached, dateRange))
                {
                    if (selectedIsInternAccount && (cached?.Donations?.Count ?? 0) == 0)
                    {
                        _logger.LogInformation(
                            "Donation data cache stale-for-intern user='{UserId}' selectedAccount='{SelectedAccount}' rows=0 range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}; refreshing from DB",
                            userId,
                            result.SelectedAccount,
                            dateRange.StartDate,
                            dateRange.EndDate);
                    }
                    else
                    {
                    // Use cached DTO but still ensure UserAccounts & selection are present
                    cached.UserAccounts = result.UserAccounts;
                    cached.SelectedAccount = result.SelectedAccount;
                    _logger.LogInformation(
                        "Donation data source=cache user='{UserId}' selectedAccount='{SelectedAccount}' rows={RowCount} range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd} elapsedMs={ElapsedMs}",
                        userId,
                        cached.SelectedAccount,
                        cached.Donations?.Count ?? 0,
                        dateRange.StartDate,
                        dateRange.EndDate,
                        sw.ElapsedMilliseconds);
                    return cached;
                    }
                }

                if (cached != null)
                {
                    _logger.LogInformation(
                        "Donation data cache miss user='{UserId}' selectedAccount='{SelectedAccount}' reason=date-range-mismatch requested={RequestedStart:yyyy-MM-dd}..{RequestedEnd:yyyy-MM-dd} cached={CachedStart:yyyy-MM-dd}..{CachedEnd:yyyy-MM-dd} bypassSubAccountCache={BypassSubAccountCache} forceRefresh={ForceRefresh}",
                        userId,
                        result.SelectedAccount,
                        dateRange.StartDate,
                        dateRange.EndDate,
                        cached.CachedStartDate,
                        cached.CachedEndDate,
                        bypassSubAccountCache,
                        forceRefresh);
                }
            }

            _logger.LogInformation(
                "Donation data cache bypass user='{UserId}' requestedAccount='{RequestedAccount}' selectedAccount='{SelectedAccount}' bypassSubAccountCache={BypassSubAccountCache} forceRefresh={ForceRefresh} subAccountSelection='{SubAccountSelection}'",
                userId,
                accountName,
                result.SelectedAccount,
                bypassSubAccountCache,
                forceRefresh,
                normalizedSubSelection);

            // Query donation records from the shared snapshot when available; retain the existing repository path otherwise.
            var donationRecords = new List<Cya2.Core.ReadModels.DonationRecord>();
            if (accountSnapshot != null && selected != null && !selectedIsInternAccount &&
                (!result.ShowSubAccountDropdown || string.Equals(result.SelectedSubAccount, "Primary", StringComparison.OrdinalIgnoreCase)))
            {
                var snapshotDonations = accountSnapshot.Donations
                    .Select(ToDonationRecord)
                    .Where(r => r.Date.Date >= dateRange.StartDate.Date && r.Date.Date <= dateRange.EndDate.Date);
                donationRecords = snapshotDonations.ToList();
            }
            else if (accountSnapshot != null && selectedIsInternAccount)
            {
                donationRecords = accountSnapshot.Donations
                    .Select(ToDonationRecord)
                    .Where(r => r.Date.Date >= dateRange.StartDate.Date && r.Date.Date <= dateRange.EndDate.Date)
                    .ToList();
            }
            else if (selected != null && !string.IsNullOrWhiteSpace(selected.Fund))
            {
                if (selectedIsInternAccount)
                {
                    if (InternAccountUtility.TryGetInternDesignationName(selected.Fund, out var internDesignationName))
                    {
                        donationRecords = await _donationReadRepository.GetInternDonationsByDesignationAndDateRangeAsync(
                            internDesignationName,
                            queryRange.StartDate,
                            queryRange.EndDate)
                            ?? new List<Cya2.Core.ReadModels.DonationRecord>();
                    }
                }
                else if (result.ShowSubAccountDropdown)
                {
                    var fundsForSelection = new List<string>();
                    if (string.Equals(result.SelectedSubAccount, "Primary", StringComparison.OrdinalIgnoreCase))
                    {
                        fundsForSelection.Add(selected.Fund);
                    }
                    else if (string.Equals(result.SelectedSubAccount, "All", StringComparison.OrdinalIgnoreCase))
                    {
                        fundsForSelection.Add(selected.Fund);
                        fundsForSelection.AddRange(separateSubAccounts.Select(sa => sa.SubFund));
                    }
                    else if (result.SelectedSubAccount.StartsWith("Sub_", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(result.SelectedSubAccount[4..], out var subId))
                    {
                        var chosen = separateSubAccounts.FirstOrDefault(sa => sa.Id == subId);
                        if (chosen != null)
                            fundsForSelection.Add(chosen.SubFund);
                    }

                    if (fundsForSelection.Count == 0)
                        fundsForSelection.Add(selected.Fund);

                    donationRecords = (await _donationReadRepository.GetDonationsByFundsAndDateRangeAsync(
                            fundsForSelection.Distinct(StringComparer.OrdinalIgnoreCase),
                            queryRange.StartDate,
                            queryRange.EndDate))
                        ?? new List<Cya2.Core.ReadModels.DonationRecord>();
                }
                else
                {
                    donationRecords = (await _donationReadRepository.GetDonationsByAccountAndDateRangeAsync(
                            selected.AccountId,
                            selected.Fund,
                            queryRange.StartDate,
                            queryRange.EndDate))
                        ?? new List<Cya2.Core.ReadModels.DonationRecord>();
                }
            }
            else if (fundsToQuery.Count > 0)
            {
                donationRecords = (await _donationReadRepository.GetDonationsByFundsAndDateRangeAsync(fundsToQuery, queryRange.StartDate, queryRange.EndDate))
                    ?? new List<Cya2.Core.ReadModels.DonationRecord>();
            }

            _logger.LogInformation(
                "Donation data source={DataSource} for user '{UserId}': selectedAccount='{SelectedAccount}', requestedAccount='{RequestedAccount}', fundsQueried={FundCount}, rows={RowCount}, requestedRange={RequestedStart:yyyy-MM-dd}..{RequestedEnd:yyyy-MM-dd}, queriedRange={QueryStart:yyyy-MM-dd}..{QueryEnd:yyyy-MM-dd}, elapsedMs={ElapsedMs}",
                donationDataSource,
                userId,
                result.SelectedAccount,
                accountName,
                fundsToQuery.Count,
                donationRecords.Count,
                dateRange.StartDate,
                dateRange.EndDate,
                queryRange.StartDate,
                queryRange.EndDate,
                sw.ElapsedMilliseconds);

            // Map DonationRecord -> DonationRowDto (application DTO)
            result.Donations = donationRecords.Select(r => new DonationRowDto
            {
                Date = r.Date,
                Account = selectedIsInternAccount ? result.SelectedAccount : r.Fund,
                Donor = ResolveDonorDisplayName(r),
                Amount = r.Amount,
                TransactionType = r.PaymentMethod ?? string.Empty,
                Frequency = GetFrequencyLabel(r.Frequency),
                Email = r.Email ?? string.Empty,
                PhoneFixed = r.PhoneFixed ?? string.Empty,
                PhoneMobile = r.PhoneMobile ?? string.Empty,
                Address = r.Address ?? string.Empty,
                City = r.City ?? string.Empty,
                State = r.State ?? string.Empty,
                PostalCode = r.PostalCode ?? string.Empty,
                Country = r.Country ?? string.Empty,
                SoftCreditName = r.SoftCreditName ?? string.Empty,
                IsAnonymous = r.IsAnonymous
            }).ToList();

            // Populate fund name lists for selection / raw funds
            result.FundNamesForSelection = donationRecords.Select(r => r.Fund).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.RawDonationFunds = donationRecords.Select(r => r.Fund).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            result.CachedStartDate = queryRange.StartDate;
            result.CachedEndDate = queryRange.EndDate;

            // Store into cache for quick reuse (cache by selectedAccount) - prefer prioritize when explicit account requested
            if (!bypassSubAccountCache && !string.IsNullOrWhiteSpace(result.SelectedAccount) && _donationCache != null)
            {
                _donationCache.SetDonationData(userId, result.SelectedAccount, result, prioritize: !string.IsNullOrWhiteSpace(accountName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to load donation data for user '{UserId}' and account '{AccountName}' in range {StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}",
                userId,
                accountName,
                dateRange.StartDate,
                dateRange.EndDate);
        }

        return result;
    }

    private async Task<AccountDataSnapshot> LoadAccountSnapshotAsync(
        UserAccountContextAccount account,
        DateRange queryRange,
        AccountSnapshotKey key)
    {
        var donations = InternAccountUtility.IsInternFund(account.Fund) &&
                        InternAccountUtility.TryGetInternDesignationName(account.Fund, out var designation)
            ? await _donationReadRepository.GetInternDonationsByDesignationAndDateRangeAsync(
                designation,
                queryRange.StartDate,
                queryRange.EndDate)
            : await _donationReadRepository.GetDonationsByAccountAndDateRangeAsync(
                account.AccountId,
                account.Fund,
                queryRange.StartDate,
                queryRange.EndDate);

        var subAccounts = InternAccountUtility.IsInternFund(account.Fund)
            ? new List<Cya2.Core.Entities.SubAccount>()
            : await _donationReadRepository.GetSubAccountsByAccountIdAsync(account.AccountId);

        var donationSnapshots = (donations ?? [])
            .Select(ToDonationSnapshot)
            .ToList();
        var subAccountSnapshots = (subAccounts ?? [])
            .Select(sa => new SubAccountSnapshot(sa.Id, sa.AccountId, sa.SubFund, sa.Kind))
            .ToList();

        return new AccountDataSnapshot(
            key,
            donationSnapshots,
            [],
            subAccountSnapshots,
            DateTime.UtcNow,
            EstimateSnapshotBytes(donationSnapshots, subAccountSnapshots));
    }

    private static DonationSnapshot ToDonationSnapshot(Cya2.Core.ReadModels.DonationRecord record)
        => new(
            record.Id,
            record.Date,
            record.Frequency,
            record.AccountName,
            record.PaymentMethod,
            record.GiftType,
            record.Amount,
            record.Fund,
            record.Intern,
            record.HonorMemorialName,
            record.Addressee,
            record.SoftCreditName,
            record.Address,
            record.City,
            record.State,
            record.PostalCode,
            record.Country,
            record.Email,
            record.PhoneFixed,
            record.PhoneMobile,
            record.DateCreated,
            record.IsAnonymous);

    private static Cya2.Core.ReadModels.DonationRecord ToDonationRecord(DonationSnapshot snapshot)
        => new()
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

    private static long EstimateSnapshotBytes(
        IReadOnlyCollection<DonationSnapshot> donations,
        IReadOnlyCollection<SubAccountSnapshot> subAccounts)
        => (donations.Count * 512L) + (subAccounts.Count * 128L);

    private static bool CacheCoversRequestedRange(DonationDataDto cached, DateRange requested)
    {
        if (cached == null) return false;
        if (cached.CachedStartDate == default || cached.CachedEndDate == default) return false;

        return cached.CachedStartDate.Date <= requested.StartDate.Date &&
               cached.CachedEndDate.Date   >= requested.EndDate.Date;
    }

    private static DateRange GetSessionCacheQueryRange(DateRange requested)
    {
        var now = DateTime.UtcNow;
        var baselineStart = new DateTime(now.Year - 2, 1, 1);
        var baselineEnd = new DateTime(now.Year, 12, 31);

        var start = requested.StartDate.Date < baselineStart ? requested.StartDate.Date : baselineStart;
        var end = requested.EndDate.Date > baselineEnd ? requested.EndDate.Date : baselineEnd;

        if ((end - start).TotalDays > 1461)
        {
            return new DateRange(requested.StartDate.Date, requested.EndDate.Date);
        }

        return new DateRange(start, end);
    }

    private static bool IsCanonicalSnapshotRange(DateRange range)
    {
        var now = DateTime.UtcNow;
        return range.StartDate.Date == new DateTime(now.Year - 2, 1, 1) &&
               range.EndDate.Date == new DateTime(now.Year, 12, 31);
    }

    private static string GetFrequencyLabel(Cya2.Core.Enums.DonorFrequency? frequency) => frequency switch
    {
        Cya2.Core.Enums.DonorFrequency.OneTime  => "One-time",
        Cya2.Core.Enums.DonorFrequency.Sporadic => "Sporadic",
        Cya2.Core.Enums.DonorFrequency.Monthly  => "Monthly",
        Cya2.Core.Enums.DonorFrequency.Quarterly => "Quarterly",
        Cya2.Core.Enums.DonorFrequency.Yearly   => "Yearly",
        _                                        => string.Empty
    };

    private static string ResolveDonorDisplayName(Cya2.Core.ReadModels.DonationRecord record)
    {
        var accountName = NormalizeNameValue(record.AccountName);
        var addressee = NormalizeNameValue(record.Addressee);
        var softCreditName = NormalizeNameValue(record.SoftCreditName);

        if (!string.IsNullOrWhiteSpace(softCreditName) &&
            !ContainsName(accountName, softCreditName) &&
            !ContainsName(addressee, softCreditName))
        {
            return !string.IsNullOrWhiteSpace(accountName)
                ? $"{softCreditName} (via {accountName})"
                : softCreditName;
        }

        if (!string.IsNullOrWhiteSpace(addressee))
        {
            return addressee;
        }

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            return accountName;
        }

        return record.IsAnonymous ? string.Empty : "Unknown";
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
}
