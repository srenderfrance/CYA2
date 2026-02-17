using Cya2.Core.Entities;

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
    public DonorFrequency Frequency { get; set; }
    public string FrequencyDisplay { get; set; } = string.Empty;
    public bool HasAlerts { get; set; }
    public string AlertSummary { get; set; } = string.Empty;
}