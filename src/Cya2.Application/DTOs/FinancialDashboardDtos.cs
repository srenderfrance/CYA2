using Cya2.Core.ValueObjects;

namespace Cya2.Application.DTOs;

/// <summary>
/// Complete financial dashboard data for Home page
/// </summary>
public class FinancialDashboardDto
{
    public string SelectedAccount { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsAdminUser { get; set; }
    public bool HasAccountData { get; set; }
    public List<UserAccountDto> UserAccounts { get; set; } = new();
    
    // Financial summary periods
    public FinancialSummaryDto CurrentMonth { get; set; } = new();
    public FinancialSummaryDto PriorMonth { get; set; } = new();
    public FinancialSummaryDto CurrentYear { get; set; } = new();
    public FinancialSummaryDto PriorYear { get; set; } = new();

    // Embedded donation payloads for initial page load (selected + default account)
    public DonationDataDto? SelectedAccountDonations { get; set; }
    public DonationDataDto? DefaultAccountDonations { get; set; }
}

/// <summary>
/// Financial summary for a specific time period
/// </summary>
public class FinancialSummaryDto
{
    public decimal TotalDonations { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal TotalOverhead { get; set; }
    public decimal InternalTransfers { get; set; }
    public decimal AvgMonthlyDonations { get; set; }
    public decimal AvgMonthlyExpenses { get; set; }
    public decimal Balance { get; set; }
    public string Period { get; set; } = string.Empty;
}

public class MonthlyAccountVisualizationDto
{
    public DateTime MonthStart { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public decimal DonationTotal { get; set; }
    public decimal ExpenseTotal { get; set; }
    public decimal Balance { get; set; }
}

/// <summary>
/// User account information for dropdown
/// </summary>
public class UserAccountDto
{
    public int AccountId { get; set; }
    public string Fund { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Overhead { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// Simple test DTO to verify namespace compilation
/// </summary>
public class SimpleTestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}