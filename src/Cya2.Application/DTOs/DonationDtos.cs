using ModelsLibrary;
using UtilityClassLibrary;

namespace Cya2.Application.DTOs;

public class DonationDataDto
{
    public List<Account> UserAccounts { get; set; } = new();
    public string SelectedAccount { get; set; } = string.Empty;
    public string SelectedSubAccount { get; set; } = "All";
    public bool ShowSubAccountDropdown { get; set; }
    public List<SubAccountOptionDto> SubAccountOptions { get; set; } = new();
    public List<string> FundNamesForSelection { get; set; } = new();
    public List<string> RawDonationFunds { get; set; } = new();
    public List<UserDataDonations> Donations { get; set; } = new();
}

public class SubAccountOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string DisplayText { get; set; } = string.Empty;
    public bool IsAll { get; set; }
    public bool IsPrimary { get; set; }
    public int? SubAccountId { get; set; }
    public string? SubFund { get; set; }
}
