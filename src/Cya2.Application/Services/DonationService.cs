using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelsLibrary;
using UtilityClassLibrary;

namespace Cya2.Application.Services;

public class DonationService : IDonationService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DonationService> _logger;
    private readonly IExpenseService _expenseService;

    public DonationService(IDataAccess dataAccess, IConfiguration configuration, ILogger<DonationService> logger, IExpenseService expenseService)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
        _expenseService = expenseService;
    }

    public async Task<DonationDataDto> GetDonationDataAsync(string accountName, string? subAccountSelection, DateRange dateRange, string userId, bool isAdminOrViewer = false)
    {
        var result = new DonationDataDto
        {
            SelectedSubAccount = string.IsNullOrWhiteSpace(subAccountSelection) ? "All" : subAccountSelection
        };

        try
        {
            var userAccounts = await _expenseService.GetUserAccountsAsync(userId, isAdminOrViewer);
            result.UserAccounts = userAccounts;
            if (!userAccounts.Any()) return result;

            var selectedAccount = userAccounts.FirstOrDefault(a => a.Fund == accountName) ?? userAccounts.First();
            result.SelectedAccount = selectedAccount.Fund;

            var subAccounts = await LoadSubAccountsAsync(selectedAccount.AccountId);
            var separateSubAccounts = subAccounts.Where(sa => string.Equals(sa.Kind, "Separate", StringComparison.OrdinalIgnoreCase)).ToList();
            var mergedSubAccounts = subAccounts.Where(sa => string.Equals(sa.Kind, "Merged", StringComparison.OrdinalIgnoreCase)).ToList();

            result.ShowSubAccountDropdown = separateSubAccounts.Any();
            result.SubAccountOptions = BuildSubAccountOptions(selectedAccount, separateSubAccounts);
            if (!result.SubAccountOptions.Any(o => string.Equals(o.Value, result.SelectedSubAccount, StringComparison.OrdinalIgnoreCase)))
            {
                result.SelectedSubAccount = "All";
            }

            var fundNamesForSelection = result.ShowSubAccountDropdown
                ? GetFundNamesForSelection(selectedAccount, separateSubAccounts, result.SelectedSubAccount)
                : GetMergedFundNames(selectedAccount, mergedSubAccounts);
            result.FundNamesForSelection = fundNamesForSelection;

            var allFundsToLoad = result.ShowSubAccountDropdown
                ? GetFundNamesForSelection(selectedAccount, separateSubAccounts, "All")
                : fundNamesForSelection;

            var donationRows = await LoadDonationsForFundsAsync(allFundsToLoad);
            result.RawDonationFunds = donationRows.Select(d => d.Fund)
                                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                                  .OrderBy(f => f)
                                                  .ToList();

            var subFundToPrimary = BuildMergedMap(selectedAccount, mergedSubAccounts);
            result.Donations = MapDonations(donationRows, selectedAccount, result.ShowSubAccountDropdown, fundNamesForSelection, subFundToPrimary, result.SelectedSubAccount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting donation data for account {AccountName}", accountName);
        }

        return result;
    }

    private async Task<List<SubAccount>> LoadSubAccountsAsync(int accountId)
    {
        try
        {
            const string sql = "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId";
            var conn = GetConnectionString();
            var rows = await _dataAccess.LoadData<SubAccount, dynamic>(sql, new { AccountId = accountId }, conn);
            return rows?.ToList() ?? new List<SubAccount>();
        }
        catch
        {
            return new List<SubAccount>();
        }
    }

    private List<SubAccountOptionDto> BuildSubAccountOptions(Account primaryAccount, List<SubAccount> separateSubAccounts)
    {
        var options = new List<SubAccountOptionDto>();

        options.Add(new SubAccountOptionDto
        {
            Value = "All",
            DisplayText = "All",
            IsAll = true,
            IsPrimary = false
        });

        options.Add(new SubAccountOptionDto
        {
            Value = "Primary",
            DisplayText = GetFundDisplay(primaryAccount.Fund),
            IsAll = false,
            IsPrimary = true,
            SubFund = primaryAccount.Fund
        });

        foreach (var sub in separateSubAccounts)
        {
            options.Add(new SubAccountOptionDto
            {
                Value = $"Sub_{sub.Id}",
                DisplayText = GetFundDisplay(sub.SubFund),
                IsPrimary = false,
                IsAll = false,
                SubAccountId = sub.Id,
                SubFund = sub.SubFund
            });
        }

        return options;
    }

    private List<string> GetFundNamesForSelection(Account primaryAccount, List<SubAccount> separateSubAccounts, string selectedValue)
    {
        var fundNames = new List<string>();

        if (string.Equals(selectedValue, "All", StringComparison.OrdinalIgnoreCase))
        {
            fundNames.Add(primaryAccount.Fund);
            fundNames.AddRange(separateSubAccounts.Select(sa => sa.SubFund));
        }
        else if (string.Equals(selectedValue, "Primary", StringComparison.OrdinalIgnoreCase))
        {
            fundNames.Add(primaryAccount.Fund);
        }
        else if (selectedValue.StartsWith("Sub_", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(selectedValue.Substring(4), out var subId))
            {
                var target = separateSubAccounts.FirstOrDefault(sa => sa.Id == subId);
                if (target != null)
                {
                    fundNames.Add(target.SubFund);
                }
            }
        }
        else
        {
            fundNames.Add(primaryAccount.Fund);
        }

        return fundNames.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> GetMergedFundNames(Account primaryAccount, List<SubAccount> mergedSubAccounts)
    {
        var fundNames = new List<string> { primaryAccount.Fund };
        fundNames.AddRange(mergedSubAccounts.Select(sa => sa.SubFund));
        return fundNames.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private Dictionary<string, int> BuildMergedMap(Account primaryAccount, List<SubAccount> mergedSubAccounts)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(primaryAccount.Fund))
        {
            map[primaryAccount.Fund] = primaryAccount.AccountId;
        }

        foreach (var sub in mergedSubAccounts)
        {
            if (!string.IsNullOrWhiteSpace(sub.SubFund) && !map.ContainsKey(sub.SubFund))
            {
                map[sub.SubFund] = primaryAccount.AccountId;
            }
        }

        return map;
    }

    private async Task<List<DonationsDataModel>> LoadDonationsForFundsAsync(IEnumerable<string> fundNames)
    {
        var result = new List<DonationsDataModel>();
        var conn = GetConnectionString();

        foreach (var fund in fundNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                const string sql = "SELECT * FROM DonationData WHERE Fund = @Fund";
                var rows = await _dataAccess.LoadData<DonationsDataModel, dynamic>(sql, new { Fund = fund }, conn);
                if (rows != null && rows.Any())
                {
                    result.AddRange(rows);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading donations for fund {Fund}", fund);
            }
        }

        return result;
    }

    private List<UserDataDonations> MapDonations(List<DonationsDataModel> donationData, Account currentAccount, bool hasSeparateSubAccounts, List<string> fundNamesForSelection, Dictionary<string, int> subFundToPrimaryAccountId, string selectedSubAccount)
    {
        var donations = new List<UserDataDonations>();
        if (currentAccount == null) return donations;

        foreach (var donation in donationData)
        {
            bool shouldInclude = false;
            string displayAccountName = currentAccount.Fund;

            if (hasSeparateSubAccounts)
            {
                if (fundNamesForSelection.Contains(donation.Fund, StringComparer.OrdinalIgnoreCase))
                {
                    shouldInclude = true;
                    displayAccountName = (!string.Equals(selectedSubAccount, "All", StringComparison.OrdinalIgnoreCase) && !string.Equals(selectedSubAccount, "Primary", StringComparison.OrdinalIgnoreCase))
                        ? GetFundDisplay(donation.Fund)
                        : GetFundDisplay(currentAccount.Fund);
                }
            }
            else
            {
                if (subFundToPrimaryAccountId.TryGetValue(donation.Fund, out var mappedPrimaryId) && mappedPrimaryId == currentAccount.AccountId)
                {
                    shouldInclude = true;
                    displayAccountName = GetFundDisplay(currentAccount.Fund);
                }
            }

            if (!shouldInclude)
            {
                continue;
            }

            var donorDisplay = donation.IsAnonymous ? "Anonymous" : donation.AccountName;
            var email = donation.IsAnonymous ? string.Empty : (donation.Email ?? string.Empty);
            var phoneFixed = donation.IsAnonymous ? string.Empty : (donation.PhoneFixed ?? string.Empty);
            var phoneMobile = donation.IsAnonymous ? string.Empty : (donation.PhoneMobile ?? string.Empty);
            var addr = donation.IsAnonymous ? string.Empty : (donation.Address ?? string.Empty);
            var city = donation.IsAnonymous ? string.Empty : (donation.City ?? string.Empty);
            var state = donation.IsAnonymous ? string.Empty : (donation.State ?? string.Empty);
            var postal = donation.IsAnonymous ? string.Empty : (donation.PostalCode ?? string.Empty);
            var country = donation.IsAnonymous ? string.Empty : (donation.Country ?? string.Empty);
            var soft = donation.IsAnonymous ? string.Empty : (donation.SoftCreditName ?? string.Empty);

            donations.Add(new UserDataDonations(
                displayAccountName,
                donorDisplay ?? string.Empty,
                donation.Date,
                donation.Amount,
                donation.PaymentMethod ?? string.Empty,
                donation.GiftType ?? string.Empty,
                email,
                phoneFixed,
                phoneMobile,
                addr,
                city,
                state,
                postal,
                country,
                soft,
                donation.IsAnonymous));
        }

        return donations.OrderByDescending(d => d.Date).ToList();
    }

    private string GetFundDisplay(string? fund)
    {
        if (string.IsNullOrWhiteSpace(fund)) return string.Empty;
        var idx = fund.IndexOf(" :", StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = fund.IndexOf(':');
        }
        var before = idx >= 0 ? fund.Substring(0, idx) : fund;
        return before.TrimEnd();
    }

    private string GetConnectionString() => _configuration.GetConnectionString("default") ?? string.Empty;
}
