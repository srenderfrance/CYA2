using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

public class AccountDto
{
    public int Id { get; set; }
    public string FundCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public decimal CurrentBalance { get; set; }
    public List<SubAccountDto> SubAccounts { get; set; } = new();
    public int DonationCount { get; set; }
    public decimal TotalDonations { get; set; }
}