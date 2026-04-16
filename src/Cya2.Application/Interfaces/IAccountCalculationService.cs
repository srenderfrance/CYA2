using Cya2.Core.ReadModels;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for performing account balance and financial calculations
/// </summary>
public interface IAccountCalculationService
{
    /// <summary>
    /// Calculate account balance using repository queries with transaction categorization
    /// </summary>
    Task<BalanceCalculationResult> CalculateBalanceAsync(UserAccountContextAccount account, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Calculate balance using pre-loaded transaction data
    /// </summary>
    BalanceCalculationResult CalculateBalanceFromData(List<AccountingRecord> entries, decimal balanceAdjustment = 0m, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Calculate donation totals and overhead for account over date range
    /// </summary>
    Task<DonationTotalsResult> CalculateDonationTotalsAsync(UserAccountContextAccount account, DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Calculate overhead amount based on donation total and account percentage
    /// </summary>
    decimal CalculateOverheadAmount(UserAccountContextAccount account, decimal donationTotal);
}

/// <summary>
/// Result of balance calculation with categorized transactions
/// </summary>
public class BalanceCalculationResult
{
    public decimal TotalBalance { get; set; }
    public decimal ExpenseTotal { get; set; }
    public decimal TransferTotal { get; set; }
    public decimal OtherTotal { get; set; }
    public List<AccountingRecord> ExpenseTransactions { get; set; } = new();
    public List<AccountingRecord> TransferTransactions { get; set; } = new();
    public List<AccountingRecord> OtherTransactions { get; set; } = new();
    public List<AccountingRecord> AllTransactions { get; set; } = new();
}

/// <summary>
/// Result of donation totals calculation including sub-account handling
/// </summary>
public class DonationTotalsResult
{
    public int AccountId { get; set; }
    public string PrimaryFundRef { get; set; } = string.Empty;
    public string PrimaryFundName { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public decimal PrimaryDonations { get; set; }
    public decimal MergedSubfundDonations { get; set; }
    public decimal TotalDonations { get; set; }
    public decimal OverheadTotal { get; set; }
    public Dictionary<string, decimal> SeparateSubfundTotals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}