using Cya2.Application.Interfaces;
using Cya2.Core.Entities;

namespace Cya2.Application.Services;

/// <summary>
/// Service for sophisticated donor analysis including frequency patterns and catch-up donations
/// Uses the enhanced DonorProfile entity with smart frequency analysis
/// </summary>
public class DonorAnalyticsService : IDonorAnalyticsService
{
    /// <summary>
    /// Build donor profiles from donation data with sophisticated frequency analysis
    /// </summary>
    public async Task<List<DonorProfile>> BuildDonorProfilesAsync(List<Donation> allDonations, DateTime analysisStartDate, DateTime analysisEndDate)
    {
        if (allDonations == null || !allDonations.Any())
            return new List<DonorProfile>();

        var profiles = new Dictionary<string, DonorProfile>();

        // Group donations by cleaned donor name
        var donationGroups = allDonations
            .Where(d => !string.IsNullOrWhiteSpace(d.AccountName))
            .GroupBy(d => CleanDonorName(d.AccountName));

        foreach (var group in donationGroups)
        {
            var donorName = group.Key;
            var donations = group.OrderBy(d => d.Date).ToList();

            // Create profile with complete giving history
            var profile = DonorProfile.CreateFromDonations(donorName, donations);
            profiles[donorName] = profile;
        }

        // Return profiles ordered by total giving (highest first)
        return profiles.Values
            .OrderByDescending(p => p.TotalGiving)
            .ToList();
    }

    /// <summary>
    /// Get donor profile for specific donor
    /// </summary>
    public async Task<DonorProfile?> GetDonorProfileAsync(string donorName, List<Donation> allDonations, DateTime analysisStartDate, DateTime analysisEndDate)
    {
        if (string.IsNullOrWhiteSpace(donorName) || allDonations == null)
            return null;

        var cleanedName = CleanDonorName(donorName);
        var donorDonations = allDonations
            .Where(d => CleanDonorName(d.AccountName) == cleanedName)
            .OrderBy(d => d.Date)
            .ToList();

        if (!donorDonations.Any())
            return null;

        return DonorProfile.CreateFromDonations(cleanedName, donorDonations);
    }

    /// <summary>
    /// Generate alerts for missed giving patterns and frequency changes
    /// </summary>
    public async Task<List<DonorAlertResult>> GetDonorAlertsAsync(List<DonorProfile> profiles, DateTime alertStartDate, DateTime alertEndDate)
    {
        var alerts = new List<DonorAlertResult>();

        foreach (var profile in profiles)
        {
            try
            {
                var frequencyAnalysis = profile.GetFrequencyAnalysis(alertStartDate, alertEndDate);

                // Check for missed months (monthly donors only)
                if (frequencyAnalysis.HasMissedMonths)
                {
                    alerts.Add(new DonorAlertResult
                    {
                        DonorName = profile.PrimaryName,
                        AlertType = "MissedMonths",
                        Message = frequencyAnalysis.GetAlertMessage(),
                        AlertDate = DateTime.Now,
                        Severity = "Warning"
                    });
                }

                // Check for frequency changes
                if (frequencyAnalysis.HasFrequencyChanged)
                {
                    alerts.Add(new DonorAlertResult
                    {
                        DonorName = profile.PrimaryName,
                        AlertType = "FrequencyChange", 
                        Message = $"Frequency changed: {frequencyAnalysis.FrequencyChange.PreviousFrequency} → {frequencyAnalysis.FrequencyChange.CurrentFrequency}",
                        AlertDate = DateTime.Now,
                        Severity = DetermineFrequencyChangeSeverity(frequencyAnalysis.FrequencyChange)
                    });
                }
            }
            catch (Exception)
            {
                // Log error but continue processing other donors
                continue;
            }
        }

        return alerts.OrderBy(a => a.DonorName).ToList();
    }

    /// <summary>
    /// Get donor summaries for grid display with frequency analysis
    /// </summary>
    public async Task<List<DonorSummaryResult>> GetDonorSummariesAsync(List<Donation> donations, DateTime startDate, DateTime endDate)
    {
        var profiles = await BuildDonorProfilesAsync(donations, startDate, endDate);
        var alerts = await GetDonorAlertsAsync(profiles, startDate, endDate);
        var alertsByDonor = alerts.GroupBy(a => a.DonorName).ToDictionary(g => g.Key, g => g.ToList());

        var summaries = new List<DonorSummaryResult>();

        foreach (var profile in profiles)
        {
            var donationsInRange = profile.GetDonationsInRange(startDate, endDate);
            if (!donationsInRange.Any()) continue;

            var frequencyAnalysis = profile.GetFrequencyAnalysis(startDate, endDate);
            var hasAlerts = alertsByDonor.ContainsKey(profile.PrimaryName);
            
            var summary = new DonorSummaryResult
            {
                Name = profile.GetSafeDisplayName(),
                Total = profile.GetGivingInRange(startDate, endDate),
                LastDonation = profile.LastGiftDate,
                Email = profile.MostRecentContact?.Email ?? string.Empty,
                PhoneSummary = GetPhoneSummary(profile.MostRecentContact),
                AddressSummary = profile.MostRecentContact?.Address?.GetDisplayAddress() ?? string.Empty,
                PaymentMethod = GetMostCommonPaymentMethod(donationsInRange),
                Frequency = frequencyAnalysis.Frequency,
                FrequencyDisplay = GetFrequencyDisplay(frequencyAnalysis),
                HasAlerts = hasAlerts,
                AlertSummary = hasAlerts ? string.Join("; ", alertsByDonor[profile.PrimaryName].Select(a => a.Message)) : string.Empty
            };

            summaries.Add(summary);
        }

        return summaries.OrderByDescending(s => s.Total).ToList();
    }

    // Helper methods
    private static string CleanDonorName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        
        return name.Trim()
                   .ToUpperInvariant()
                   .Replace(".", "")
                   .Replace(",", "")
                   .Replace("  ", " ");
    }

    private static string DetermineFrequencyChangeSeverity(FrequencyChangeResult change)
    {
        // Determine severity based on the nature of frequency change
        if (change.PreviousFrequency == DonorFrequency.Monthly && change.CurrentFrequency != DonorFrequency.Monthly)
            return "Warning"; // Monthly donors stopping is concerning
        
        if (change.PreviousFrequency != DonorFrequency.OneTime && change.CurrentFrequency == DonorFrequency.OneTime)
            return "Warning"; // Regular donors becoming one-time is concerning
            
        return "Info"; // Other changes are informational
    }

    private static string GetPhoneSummary(ContactInfo? contact)
    {
        if (contact == null) return string.Empty;

        var phones = new List<string>();
        if (!string.IsNullOrWhiteSpace(contact.PhoneMobile))
            phones.Add($"Cell: {contact.PhoneMobile}");
        if (!string.IsNullOrWhiteSpace(contact.PhoneFixed))
            phones.Add($"Home: {contact.PhoneFixed}");

        return string.Join("; ", phones);
    }

    private static string GetMostCommonPaymentMethod(List<Donation> donations)
    {
        return donations
            .GroupBy(d => d.PaymentMethod)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? string.Empty;
    }

    private static string GetFrequencyDisplay(DonorFrequencyResult analysis)
    {
        var display = analysis.Frequency.ToString();
        
        if (analysis.HasMissedMonths || analysis.HasFrequencyChanged)
        {
            display += " ⚠️"; // Add warning icon
        }
        
        return display;
    }
}