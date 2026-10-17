using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class DonationService : IDonationService
{
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IAccountFactDataService _accountFactDataService;
    private readonly IDonorContactHydrationService _donorContactHydrationService;
    private readonly ILogger<DonationService> _logger;
    private readonly IUserAuthorizationService _authorizationService;

    public DonationService(
        IUserAccountContextService userAccountContextService,
        IAccountFactDataService accountFactDataService,
        IDonorContactHydrationService donorContactHydrationService,
        ILogger<DonationService> logger,
        IUserAuthorizationService authorizationService)
    {
        _userAccountContextService = userAccountContextService;
        _accountFactDataService = accountFactDataService;
        _donorContactHydrationService = donorContactHydrationService;
        _logger = logger;
        _authorizationService = authorizationService;
    }

    public async Task<DonationDataDto> GetDonationDataAsync(string accountName, string? subAccountSelection, DateRange dateRange, string userId, bool isAdminOrViewer = false, bool forceRefresh = false)
    {
        var result = new DonationDataDto();
        dateRange = NormalizeDateRange(dateRange);
        var normalizedSubSelection = string.IsNullOrWhiteSpace(subAccountSelection) ? "All" : subAccountSelection;

        try
        {
            var authorization = string.IsNullOrWhiteSpace(accountName)
                ? await _authorizationService.ValidateUserAsync(userId)
                : await _authorizationService.ValidateAccountAccessAsync(userId, accountName);
            if (!authorization.IsAuthorized)
                return result;

            var context = await _userAccountContextService.GetContextAsync(userId, isAdminOrViewer);
            if (context == null || (!context.IsIntern && (context.Accounts == null || context.Accounts.Count == 0)))
            {
                _logger.LogWarning("Donation context unavailable for user '{UserId}' (isAdminOrViewer={IsAdminOrViewer})", userId, isAdminOrViewer);
                return result; // no accounts available
            }

            // Map available accounts
            result.UserAccounts = (context.Accounts ?? new List<UserAccountContextAccount>()).Select(a => new AccountOptionDto
            {
                AccountId = a.AccountId,
                Fund = a.Fund,
                InternName = context.IsIntern ? context.InternName : a.InternName,
                DisplayName = context.IsIntern
                    ? context.InternName
                    : string.IsNullOrWhiteSpace(a.InternName) ? a.DisplayName : a.InternName,
                AccountingClass = a.AccountingClass,
                AccountNumber = a.AccountNumber,
                Overhead = a.Overhead
            }).GroupBy(a => a.SelectionKey, StringComparer.OrdinalIgnoreCase)
              .Select(group => group.First())
              .ToList();

            // Resolve selected account preference using context helper
            var selected = context.IsIntern
                ? new UserAccountContextAccount { InternName = context.InternName }
                : _userAccountContextService.ResolveSelectedAccount(context, string.IsNullOrWhiteSpace(accountName) ? null : accountName);
            if (selected != null)
            {
                result.SelectedAccount = string.IsNullOrWhiteSpace(selected.InternName) ? selected.Fund : selected.InternName;
            }
            else if (!string.IsNullOrWhiteSpace(accountName) && result.UserAccounts.Any(u => string.Equals(u.SelectionKey, accountName, StringComparison.OrdinalIgnoreCase)))
            {
                result.SelectedAccount = accountName;
            }
            else
            {
                // fallback to first available account for UI only
                result.SelectedAccount = result.UserAccounts.FirstOrDefault()?.SelectionKey ?? string.Empty;
            }

            result.SelectedSubAccount = normalizedSubSelection;
            var selectedIsInternAccount = context.IsIntern || (selected != null && InternAccountUtility.IsInternFund(selected.Fund));
            var selectedData = selected is null
                ? new DonationCalculationData()
                : await _accountFactDataService.GetDonationDataAsync(
                    selected,
                    userId,
                    dateRange.StartDate,
                    dateRange.EndDate);
            // Build sub-account options for the selected account when Separate sub-accounts exist.
            var separateSubAccounts = new List<Cya2.Core.Entities.SubAccount>();
            var mergedSubAccounts = new List<Cya2.Core.Entities.SubAccount>();
            if (selected != null && !selectedIsInternAccount)
            {
                var allSubAccounts = selectedData.SubAccounts;
                separateSubAccounts = allSubAccounts
                    .Where(sa => string.Equals(sa.Kind, "Separate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sa.SubFund))
                    .ToList();
                mergedSubAccounts = allSubAccounts
                    .Where(sa => string.Equals(sa.Kind, "Merged", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sa.SubFund))
                    .ToList();

                // _logger.LogInformation(
                //     "Donation subaccounts loaded: accountId={AccountId}, fund={Fund}, total={Total}, separate={Separate}, merged={Merged}, kinds={Kinds}, separateFunds={SeparateFunds}, mergedFunds={MergedFunds}",
                //     selected.AccountId,
                //     selected.Fund,
                //     allSubAccounts.Count,
                //     separateSubAccounts.Count,
                //     mergedSubAccounts.Count,
                //     string.Join("|", allSubAccounts.Select(sa => sa.Kind?.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase)),
                //     string.Join("|", separateSubAccounts.Select(sa => sa.SubFund)),
                //     string.Join("|", mergedSubAccounts.Select(sa => sa.SubFund)));

                result.ShowSubAccountDropdown = separateSubAccounts.Any();
                // _logger.LogInformation(
                //     "Donation subaccount UI scope: accountId={AccountId}, showDropdown={ShowDropdown}, selectedSubAccount={SelectedSubAccount}",
                //     selected.AccountId,
                //     result.ShowSubAccountDropdown,
                //     result.SelectedSubAccount);
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

            var selectedFunds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selected != null && !selectedIsInternAccount)
            {
                if (string.Equals(result.SelectedSubAccount, "All", StringComparison.OrdinalIgnoreCase))
                {
                    selectedFunds.Add(selected.Fund);
                    selectedFunds.UnionWith(mergedSubAccounts.Select(sa => sa.SubFund));
                    selectedFunds.UnionWith(separateSubAccounts.Select(sa => sa.SubFund));
                }
                else if (string.Equals(result.SelectedSubAccount, "Primary", StringComparison.OrdinalIgnoreCase))
                {
                    selectedFunds.Add(selected.Fund);
                    selectedFunds.UnionWith(mergedSubAccounts.Select(sa => sa.SubFund));
                }
                else if (result.SelectedSubAccount.StartsWith("Sub_", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(result.SelectedSubAccount[4..], out var subAccountId))
                {
                    var subAccount = separateSubAccounts.FirstOrDefault(sa => sa.Id == subAccountId);
                    if (subAccount != null)
                    {
                        selectedFunds.Add(subAccount.SubFund);
                    }
                }
            }

            var donationRecords = selectedData.Donations
                .Where(record => selectedIsInternAccount || selectedFunds.Contains(record.Fund))
                .ToList();

            await _donorContactHydrationService.HydrateAsync(donationRecords);

            result.DonorContacts = donationRecords
                .Where(record => record.DonorId > 0)
                .GroupBy(record => record.DonorId)
                .ToDictionary(
                    group => group.Key,
                    group => new DonorContactDto
                    {
                        Email = JoinContactValues(group.Select(record => record.Email)),
                        PhoneFixed = JoinContactValues(group.Select(record => record.PhoneFixed)),
                        PhoneMobile = JoinContactValues(group.Select(record => record.PhoneMobile)),
                        Address = group
                            .OrderByDescending(record => record.Date)
                            .Select(record => record.Address)
                            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                        City = JoinContactValues(group.Select(record => record.City)),
                        State = JoinContactValues(group.Select(record => record.State)),
                        PostalCode = JoinContactValues(group.Select(record => record.PostalCode)),
                        Country = JoinContactValues(group.Select(record => record.Country))
                    });

            // Map DonationRecord -> DonationRowDto (application DTO)
            result.Donations = donationRecords.Select(r => new DonationRowDto
            {
                Date = r.Date,
                Account = selectedIsInternAccount ? result.SelectedAccount : r.Fund,
                DonorId = r.DonorId,
                Donor = ResolveDonorDisplayName(r),
                Amount = r.Amount,
                TransactionType = r.PaymentMethod ?? string.Empty,
                Frequency = GetFrequencyLabel(r.Frequency),
                SoftCreditName = r.SoftCreditName ?? string.Empty,
                IsAnonymous = r.IsAnonymous
            }).ToList();

            // Populate fund name lists for selection / raw funds
            result.FundNamesForSelection = donationRecords.Select(r => r.Fund).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.RawDonationFunds = donationRecords.Select(r => r.Fund).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            result.CachedStartDate = dateRange.StartDate;
            result.CachedEndDate = dateRange.EndDate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to load donation data for user '{UserId}' in range {StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}",
                userId,
                dateRange.StartDate,
                dateRange.EndDate);
        }

        return result;
    }

    private static string JoinContactValues(IEnumerable<string?> values)
        => string.Join(", ", values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static DateRange NormalizeDateRange(DateRange range)
    {
        return new DateRange(range.StartDate.Date, EndOfDay(range.EndDate));
    }

    private static DateTime EndOfDay(DateTime date)
        => date.Date.AddDays(1).AddTicks(-1);

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
