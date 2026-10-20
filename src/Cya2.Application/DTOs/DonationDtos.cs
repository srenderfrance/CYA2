namespace Cya2.Application.DTOs;

public class DonationDataDto
{
    public List<AccountOptionDto> UserAccounts { get; set; } = new();
    public bool IsInternUser { get; set; }
    public bool CanSearchAccounts { get; set; }
    public string SelectedAccount { get; set; } = string.Empty;
    public string SelectedSubAccount { get; set; } = "All";
    public bool ShowSubAccountDropdown { get; set; }
    public List<SubAccountOptionDto> SubAccountOptions { get; set; } = new();
    public List<string> FundNamesForSelection { get; set; } = new();
    public List<string> RawDonationFunds { get; set; } = new();
    public List<DonationRowDto> Donations { get; set; } = new();
    public Dictionary<long, DonorContactDto> DonorContacts { get; set; } = new();
    public DateTime CachedStartDate { get; set; }
    public DateTime CachedEndDate { get; set; }
    public long DonationsGeneration { get; set; }
    public long DonorContactsGeneration { get; set; }
}

public class DonationRowDto
{
    public string Account { get; set; } = string.Empty;
    public long DonorId { get; set; }
    public string Donor { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string SoftCreditName { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
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
