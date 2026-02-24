using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using DataLibrary;

namespace Cya2.Application.Services;

/// <summary>
/// Service for donation data analysis and aggregation
/// Moves complex pivot and aggregation logic from Donations.razor component
/// </summary>
public class DonationAnalyticsService : IDonationAnalyticsService
{
    private readonly IDataAccess _dataAccess;

    public DonationAnalyticsService(IDataAccess dataAccess)
    {
        _dataAccess = dataAccess;
    }

    /// <summary>
    /// Build pivot table data for donation grid display - moved from Donations.razor BuildPivot()
    /// </summary>
    public async Task<DonationPivotResult> BuildDonationPivotAsync(List<Donation> donations, DateTime startDate, DateTime endDate)
    {
        var result = new DonationPivotResult();

        // Build month columns
        var start = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        for (var cursor = start; cursor <= end; cursor = cursor.AddMonths(1))
        {
            result.MonthColumns.Add(cursor);
        }

        if (!donations.Any()) 
            return result;

        // Group by donor and build pivot rows
        var byDonor = donations
            .GroupBy(d => d.GetSafeDisplayName()) // Use entity method for safe display
            .OrderBy(g => g.Key);

        foreach (var donorGroup in byDonor)
        {
            var row = new DonationPivotRow { Donor = donorGroup.Key };

            foreach (var donation in donorGroup)
            {
                var monthKey = new DateTime(donation.Date.Year, donation.Date.Month, 1);
                if (!row.Monthly.ContainsKey(monthKey))
                {
                    row.Monthly[monthKey] = 0m;
                }
                row.Monthly[monthKey] += (decimal)donation.Amount;
            }

            result.PivotRows.Add(row);
        }

        // Calculate month totals
        foreach (var month in result.MonthColumns)
        {
            decimal total = 0m;
            foreach (var row in result.PivotRows)
            {
                if (row.Monthly.TryGetValue(month, out var amt))
                {
                    total += amt;
                }
            }
            result.MonthTotals[month] = total;
        }

        result.GrandTotal = result.MonthTotals.Values.Sum();

        return result;
    }

    /// <summary>
    /// Get comprehensive donation summary for account and date range
    /// </summary>
    public async Task<DonationSummaryResult> GetDonationSummaryAsync(int accountId, DateTime startDate, DateTime endDate)
    {
        // This would integrate with your existing data loading patterns
        // For now, return a basic structure that can be enhanced
        return new DonationSummaryResult
        {
            AccountId = accountId,
            StartDate = startDate,
            EndDate = endDate
        };
    }

    /// <summary>
    /// Filter donations by account and sub-account criteria
    /// Moves filtering logic from Donations.razor ProcessDonationData()
    /// </summary>
    public async Task<List<Donation>> FilterDonationsAsync(List<Donation> donations, Account account, string? subAccountSelection = null)
    {
        if (donations == null || !donations.Any())
            return new List<Donation>();

        if (account == null)
            return donations;

        var filtered = new List<Donation>();

        // Apply account and sub-account filtering based on Fund matching
        foreach (var donation in donations)
        {
            bool shouldInclude = false;

            // Basic fund matching - this would be enhanced based on your SubAccountHelper logic
            if (string.Equals(donation.Fund, account.Fund, StringComparison.OrdinalIgnoreCase))
            {
                shouldInclude = true;
            }

            if (shouldInclude)
            {
                // Apply anonymity protection
                if (donation.IsAnonymous)
                {
                    donation.EnsureAnonymityProtection();
                }

                filtered.Add(donation);
            }
        }

        return filtered.OrderByDescending(d => d.Date).ToList();
    }

    // Implement missing interface methods
    public async Task<DonorAnalyticsResult> AnalyzeDonorPerformanceAsync(string accountFund, DateTime fromDate, DateTime toDate)
    {
        // Placeholder implementation
        return new DonorAnalyticsResult
        {
            AccountFund = accountFund,
            AnalysisDate = DateTime.Now,
            TotalDonors = 0,
            TotalGiving = 0,
            AverageGiving = 0,
            Insights = new List<DonorInsight>()
        };
    }

    public async Task<List<DonorTrendDto>> GetDonorTrendsAsync(string accountFund, int monthsBack = 12)
    {
        // Placeholder implementation
        return new List<DonorTrendDto>();
    }

    public async Task<DonorRetentionReport> AnalyzeRetentionAsync(string accountFund, DateTime fromDate, DateTime toDate)
    {
        // Placeholder implementation
        return new DonorRetentionReport
        {
            AccountFund = accountFund,
            AnalysisPeriod = DateTime.Now,
            RetentionRate = 0,
            NewDonors = 0,
            RetainedDonors = 0,
            LapsedDonors = 0,
            Insights = new List<RetentionInsight>()
        };
    }

    public async Task<List<DonorSegmentDto>> SegmentDonorsAsync(string accountFund)
    {
        // Placeholder implementation
        return new List<DonorSegmentDto>();
    }
}