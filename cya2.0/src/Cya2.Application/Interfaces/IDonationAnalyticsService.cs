using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for donation data analysis and aggregation  
/// </summary>
public interface IDonationAnalyticsService
{
    /// <summary>
    /// Build pivot table data for donation grid display
    /// </summary>
    Task<DonationPivotResult> BuildDonationPivotAsync(List<Donation> donations, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Get donation summary for selected account and date range
    /// </summary>
    Task<DonationSummaryResult> GetDonationSummaryAsync(int accountId, DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Filter donations by account and sub-account criteria
    /// </summary>
    Task<List<Donation>> FilterDonationsAsync(List<Donation> donations, Account account, string? subAccountSelection = null);
}

/// <summary>
/// Result of donation pivot table building
/// </summary>
public class DonationPivotResult
{
    public List<DateTime> MonthColumns { get; set; } = new();
    public List<DonationPivotRow> PivotRows { get; set; } = new();
    public Dictionary<DateTime, decimal> MonthTotals { get; set; } = new();
    public decimal GrandTotal { get; set; }
}

/// <summary>
/// Individual row in donation pivot table
/// </summary>
public class DonationPivotRow
{
    public string Donor { get; set; } = string.Empty;
    public Dictionary<DateTime, decimal> Monthly { get; } = new();
    public decimal Total => Monthly.Values.Sum();
}

/// <summary>
/// Summary of donations for an account and date range
/// </summary>
public class DonationSummaryResult
{
    public int AccountId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TotalAmount { get; set; }
    public int TotalCount { get; set; }
    public decimal AverageAmount { get; set; }
    public List<Donation> RecentDonations { get; set; } = new();
    public Dictionary<string, int> DonorFrequency { get; set; } = new();
}