using ModelsLibrary;

namespace Cya2.Application.DTOs;

/// <summary>
/// Account-related DTOs
/// </summary>
public class AccountSummaryDto
{
    public string Fund { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public DateTime LastActivity { get; set; }
}

public class AccountDetailDto
{
    public string Fund { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public decimal BalanceAdjustment { get; set; }
    public decimal Overhead { get; set; }
    public string SoftCredit { get; set; } = string.Empty;
}

public class AccountBalanceDto
{
    public string AccountName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalDonations { get; set; }
    public DateTime AsOfDate { get; set; }
}

public class DonorContactDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

/// <summary>
/// Donation-related DTOs
/// </summary>
public class DonationSummaryDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string DonorName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Fund { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
}

public class DonationDetailDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string DonorName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string GiftType { get; set; } = string.Empty;
    public string Fund { get; set; } = string.Empty;
    public string SoftCreditName { get; set; } = string.Empty;
    public bool IsAnonymous { get; set; }
    public DateTime DateCreated { get; set; }
}

public class DonationStatsDto
{
    public decimal TotalAmount { get; set; }
    public int TotalCount { get; set; }
    public decimal AverageAmount { get; set; }
    public int UniqueDonors { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
}