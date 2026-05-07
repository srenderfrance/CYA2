using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Cya2.Application.Services;

public class DonationService : IDonationService
{
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly ISessionDonationDataCacheService _donationCache;
    private readonly ILogger<DonationService> _logger;

    public DonationService(
        IUserAccountContextService userAccountContextService,
        IDonationReadRepository donationReadRepository,
        ISessionDonationDataCacheService donationCache,
        ILogger<DonationService> logger)
    {
        _userAccountContextService = userAccountContextService;
        _donationReadRepository = donationReadRepository;
        _donationCache = donationCache;
        _logger = logger;
    }

    public async Task<DonationDataDto> GetDonationDataAsync(string accountName, string? subAccountSelection, DateRange dateRange, string userId, bool isAdminOrViewer = false, bool forceRefresh = false)
    {
        var sw = Stopwatch.StartNew();
        var result = new DonationDataDto();
        var normalizedSubSelection = string.IsNullOrWhiteSpace(subAccountSelection) ? "All" : subAccountSelection;
        var bypassSubAccountCache = !string.Equals(normalizedSubSelection, "All", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (!forceRefresh && !bypassSubAccountCache && !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(accountName) && _donationCache != null)
            {
                if (_donationCache.TryGetDonationData(userId, accountName, out var directCached) &&
                    CacheCoversRequestedRange(directCached, dateRange))
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

            // Build sub-account options for the selected account when Separate sub-accounts exist.
            var separateSubAccounts = new List<Cya2.Core.Entities.SubAccount>();
            if (selected != null)
            {
                var allSubAccounts = await _donationReadRepository.GetSubAccountsByAccountIdAsync(selected.AccountId);
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
            if (!forceRefresh && !bypassSubAccountCache && !string.IsNullOrWhiteSpace(result.SelectedAccount) && _donationCache != null)
            {
                if (_donationCache.TryGetDonationData(userId, result.SelectedAccount, out var cached) &&
                    CacheCoversRequestedRange(cached, dateRange))
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

            // Query donation records from read repository
            var donationRecords = new List<Cya2.Core.ReadModels.DonationRecord>();
            if (selected != null && !string.IsNullOrWhiteSpace(selected.Fund))
            {
                if (result.ShowSubAccountDropdown)
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
                            dateRange.StartDate,
                            dateRange.EndDate))
                        ?? new List<Cya2.Core.ReadModels.DonationRecord>();
                }
                else
                {
                    donationRecords = (await _donationReadRepository.GetDonationsByAccountAndDateRangeAsync(
                            selected.AccountId,
                            selected.Fund,
                            dateRange.StartDate,
                            dateRange.EndDate))
                        ?? new List<Cya2.Core.ReadModels.DonationRecord>();
                }
            }
            else if (fundsToQuery.Count > 0)
            {
                donationRecords = (await _donationReadRepository.GetDonationsByFundsAndDateRangeAsync(fundsToQuery, dateRange.StartDate, dateRange.EndDate))
                    ?? new List<Cya2.Core.ReadModels.DonationRecord>();
            }

            _logger.LogInformation(
                "Donation data source=db loaded for user '{UserId}': selectedAccount='{SelectedAccount}', requestedAccount='{RequestedAccount}', fundsQueried={FundCount}, rows={RowCount}, range={StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}, elapsedMs={ElapsedMs}",
                userId,
                result.SelectedAccount,
                accountName,
                fundsToQuery.Count,
                donationRecords.Count,
                dateRange.StartDate,
                dateRange.EndDate,
                sw.ElapsedMilliseconds);

            // Map DonationRecord -> DonationRowDto (application DTO)
            result.Donations = donationRecords.Select(r => new DonationRowDto
            {
                Date = r.Date,
                Account = r.Fund,
                Donor = string.IsNullOrWhiteSpace(r.AccountName) ? (r.IsAnonymous ? "" : "Unknown") : r.AccountName,
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

            result.CachedStartDate = dateRange.StartDate;
            result.CachedEndDate = dateRange.EndDate;

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

    private static bool CacheCoversRequestedRange(DonationDataDto cached, DateRange requested)
    {
        if (cached == null) return false;
        if (cached.CachedStartDate == default || cached.CachedEndDate == default) return false;

        return cached.CachedStartDate.Date <= requested.StartDate.Date &&
               cached.CachedEndDate.Date   >= requested.EndDate.Date;
    }

    private static string GetFrequencyLabel(Cya2.Core.Enums.DonorFrequency? frequency) => frequency switch
    {
        Cya2.Core.Enums.DonorFrequency.OneTime  => "One-time",
        Cya2.Core.Enums.DonorFrequency.Sporadic => "Sporadic",
        Cya2.Core.Enums.DonorFrequency.Monthly  => "Monthly",
        Cya2.Core.Enums.DonorFrequency.Yearly   => "Yearly",
        _                                        => string.Empty
    };
}
