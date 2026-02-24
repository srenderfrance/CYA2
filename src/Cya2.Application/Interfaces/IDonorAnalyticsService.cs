using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Enums; // Add this for DonorFrequency enum
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for donor analysis and donor profile management
/// </summary>
public interface IDonorAnalyticsService
{
    /// <summary>
    /// Build donor profiles from donation data with frequency analysis
    /// </summary>
    Task<List<DonorProfile>> BuildDonorProfilesAsync(List<Donation> allDonations, DateTime analysisStartDate, DateTime analysisEndDate);
    
    /// <summary>
    /// Get donor profile for specific donor with complete giving history
    /// </summary>
    Task<DonorProfile?> GetDonorProfileAsync(string donorName, List<Donation> allDonations, DateTime analysisStartDate, DateTime analysisEndDate);
    
    /// <summary>
    /// Analyze donor frequency changes and missed giving patterns
    /// </summary>
    Task<List<DonorAlertResult>> GetDonorAlertsAsync(List<DonorProfile> profiles, DateTime alertStartDate, DateTime alertEndDate);
    
    /// <summary>
    /// Get donor summary for grid display
    /// </summary>
    Task<List<DonorSummaryResult>> GetDonorSummariesAsync(List<Donation> donations, DateTime startDate, DateTime endDate);
    
    Task<DonorAnalyticsResult> AnalyzeDonorPerformanceAsync(string accountFund, DateTime fromDate, DateTime toDate);
    Task<List<DonorTrendDto>> GetDonorTrendsAsync(string accountFund, int monthsBack = 12);
    Task<DonorRetentionReport> AnalyzeRetentionAsync(string accountFund, DateTime fromDate, DateTime toDate);
    Task<List<DonorSegmentDto>> SegmentDonorsAsync(string accountFund);
}

/// <summary>
/// Donor alert for missed giving or frequency changes
/// </summary>
public class DonorAlertResult
{
    public string DonorName { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty; // "MissedMonths", "FrequencyChange"
    public string Message { get; set; } = string.Empty;
    public DateTime AlertDate { get; set; }
    public string Severity { get; set; } = string.Empty; // "Info", "Warning", "Error"
}

/// <summary>
/// Donor summary for grid display
/// </summary>
public class DonorSummaryResult
{
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime? LastDonation { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PhoneSummary { get; set; } = string.Empty;
    public string AddressSummary { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string FrequencyDisplay { get; set; } = string.Empty;
    public bool HasAlerts { get; set; }
    public string AlertSummary { get; set; } = string.Empty;
}

public class DonorAnalyticsResult
{
    public string AccountFund { get; set; } = string.Empty;
    public DateTime AnalysisDate { get; set; }
    public int TotalDonors { get; set; }
    public decimal TotalGiving { get; set; }
    public decimal AverageGiving { get; set; }
    public List<DonorInsight> Insights { get; set; } = new();
}

public class DonorInsight
{
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

public class DonorTrendDto
{
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int NewDonors { get; set; }
    public int ReturningDonors { get; set; }
    public decimal TotalGiving { get; set; }
    public decimal AverageGift { get; set; }
}

public class DonorRetentionReport
{
    public string AccountFund { get; set; } = string.Empty;
    public DateTime AnalysisPeriod { get; set; }
    public decimal RetentionRate { get; set; }
    public int NewDonors { get; set; }
    public int RetainedDonors { get; set; }
    public int LapsedDonors { get; set; }
    public List<RetentionInsight> Insights { get; set; } = new();
}

public class RetentionInsight
{
    public string Segment { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public string Trend { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}

public class DonorSegmentDto
{
    public string SegmentName { get; set; } = string.Empty;
    public string Criteria { get; set; } = string.Empty;
    public int DonorCount { get; set; }
    public decimal TotalGiving { get; set; }
    public decimal AverageGift { get; set; }
    // Now properly using the DonorFrequency enum
    public DonorFrequency PrimaryFrequency { get; set; }
    public string FrequencyDescription { get; set; } = string.Empty;
}