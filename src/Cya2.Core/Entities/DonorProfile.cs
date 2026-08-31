using Cya2.Core.Enums;
using Cya2.Core.Services;
using Cya2.Core.ValueObjects;

namespace Cya2.Core.Entities;

public class DonorProfile : BaseEntity
{
    public string PrimaryName { get; private set; } = string.Empty;
    public List<Donation> AllDonations { get; private set; } = new(); // Complete giving history
    public ContactInfo? MostRecentContact { get; private set; }
    
    private DonorProfile() { }
    
    public static DonorProfile CreateFromDonations(string donorName, List<Donation> donations)
    {
        var profile = new DonorProfile { PrimaryName = CleanDonorName(donorName) };
        
        foreach (var donation in donations.OrderBy(d => d.Date))
        {
            profile.AddDonation(donation);
        }
        
        return profile;
    }
    
    public void AddDonation(Donation donation)
    {
        AllDonations.Add(donation);
        
        // Update contact info from most recent non-anonymous donation
        if (!donation.IsAnonymous && (MostRecentContact == null || donation.Date > MostRecentContact.LastUpdated))
        {
            MostRecentContact = ContactInfo.FromDonation(donation);
        }
    }
    
    /// <summary>
    /// Calculate donor frequency based on COMPLETE giving history, not user's date range
    /// </summary>
    public DonorFrequencyResult GetFrequencyAnalysis(
        DateTime userStartDate,
        DateTime userEndDate,
        DonorFrequencyService frequencyService)
    {
        ArgumentNullException.ThrowIfNull(frequencyService);

        if (!AllDonations.Any())
            return new DonorFrequencyResult(DonorFrequency.None);
            
        // Use COMPLETE giving history for frequency calculation
        var firstGift = AllDonations.Min(d => d.Date);
        var lastGift = AllDonations.Max(d => d.Date);
        
        // Calculate frequency based on complete history
        var currentFrequency = frequencyService.ClassifyDonor(ToGiftRecords(AllDonations));
        
        // Check if frequency has changed by comparing recent vs. historical patterns
        var frequencyChange = DetectFrequencyChange(userStartDate, userEndDate, frequencyService);
        
        // Get missed months (accounting for catch-up donations)
        var missedMonths = currentFrequency == DonorFrequency.Monthly 
            ? GetActualMissedMonths(firstGift, lastGift) 
            : new List<DateTime>();
        
        return new DonorFrequencyResult(currentFrequency, frequencyChange, missedMonths);
    }
    
    private decimal CalculateExpectedMonthlyAmount()
    {
        // Find the most common donation amount (likely the monthly amount)
        var donationAmounts = AllDonations
            .Select(d => Math.Round((decimal)d.Amount, 2))
            .Where(a => a > 0)
            .ToList();
            
        if (!donationAmounts.Any()) return 0;
        
        // Group amounts and find the most frequent one
        var amountFrequency = donationAmounts
            .GroupBy(a => a)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key) // Prefer higher amounts in case of tie
            .FirstOrDefault();
            
        if (amountFrequency == null || amountFrequency.Count() < 2)
        {
            // If no clear pattern, use average of smaller donations (exclude large outliers)
            var sorted = donationAmounts.OrderBy(a => a).ToList();
            var q3Index = (int)(sorted.Count * 0.75);
            var reasonableAmounts = sorted.Take(q3Index + 1).ToList();
            return reasonableAmounts.Any() ? reasonableAmounts.Average() : 0;
        }
        
        return amountFrequency.Key;
    }
    
    private List<DateTime> CreateVirtualMonthlyGifts(decimal expectedAmount, DateTime start, DateTime end)
    {
        var virtualGifts = new List<DateTime>();
        var tolerancePercentage = 0.1m; // 10% tolerance for amount matching
        
        // Group donations by month
        var monthlyDonations = AllDonations
            .Where(d => IsInDateRange(d, start, end))
            .GroupBy(d => new { d.Date.Year, d.Date.Month })
            .ToDictionary(
                g => new DateTime(g.Key.Year, g.Key.Month, 1),
                g => g.Sum(d => (decimal)d.Amount)
            );
        
        foreach (var monthDonation in monthlyDonations)
        {
            var month = monthDonation.Key;
            var totalAmount = monthDonation.Value;
            
            // Calculate how many months this donation could cover
            var monthsCovered = Math.Round(totalAmount / expectedAmount);
            
            // Allow for some variance in expected amount (±10%)
            var minExpected = expectedAmount * (1 - tolerancePercentage);
            var maxReasonableMultiple = expectedAmount * 12; // Max 12 months catch-up
            
            if (totalAmount >= minExpected && totalAmount <= maxReasonableMultiple && monthsCovered >= 1)
            {
                // Add virtual gifts for each month this donation covers
                for (int i = 0; i < monthsCovered && i < 12; i++)
                {
                    var virtualMonth = month.AddMonths(-i); // Go backwards to cover missed months
                    if (virtualMonth >= start && !virtualGifts.Contains(virtualMonth))
                    {
                        virtualGifts.Add(virtualMonth);
                    }
                }
                
                // Always include the actual month
                if (!virtualGifts.Contains(month))
                    virtualGifts.Add(month);
            }
        }
        
        return virtualGifts.Distinct().OrderBy(d => d).ToList();
    }
    
    private List<DateTime> GetActualMissedMonths(DateTime firstGift, DateTime lastGift)
    {
        var expectedAmount = CalculateExpectedMonthlyAmount();
        if (expectedAmount <= 0) return new List<DateTime>();
        
        var virtualMonthlyGifts = CreateVirtualMonthlyGifts(expectedAmount, firstGift, lastGift);
        return FindMissedMonths(virtualMonthlyGifts, firstGift, lastGift);
    }
    
    private FrequencyChangeResult DetectFrequencyChange(
        DateTime userStartDate,
        DateTime userEndDate,
        DonorFrequencyService frequencyService)
    {
        // Compare recent pattern (user's date range) vs historical pattern
        var recentDonations = AllDonations.Where(d => IsInDateRange(d, userStartDate, userEndDate)).ToList();
        var historicalDonations = AllDonations.Where(d => d.Date < userStartDate).ToList();
        
        if (!historicalDonations.Any() || !recentDonations.Any())
            return new FrequencyChangeResult(false, DonorFrequency.None, DonorFrequency.None);
            
        // Create temporary profiles for comparison
        var historicalProfile = CreateFromDonations("temp", historicalDonations);
        var recentProfile = CreateFromDonations("temp", recentDonations);
        
        var historicalFreq = frequencyService.ClassifyDonor(ToGiftRecords(historicalProfile.AllDonations));
        var recentFreq = frequencyService.ClassifyDonor(ToGiftRecords(recentProfile.AllDonations));
        
        var hasChanged = historicalFreq != recentFreq && 
                        historicalFreq != DonorFrequency.None && 
                        recentFreq != DonorFrequency.None;
        
        return new FrequencyChangeResult(hasChanged, historicalFreq, recentFreq);
    }

    private static List<DonorGiftRecord> ToGiftRecords(IEnumerable<Donation> donations)
    {
        return donations
            .Select(d => new DonorGiftRecord
            {
                Date = d.Date,
                Amount = (decimal)d.Amount
            })
            .ToList();
    }
    
    private List<DateTime> FindMissedMonths(List<DateTime> giftMonths, DateTime start, DateTime end)
    {
        var missedMonths = new List<DateTime>();
        var current = new DateTime(start.Year, start.Month, 1);
        var endMonth = new DateTime(end.Year, end.Month, 1);
        
        while (current <= endMonth)
        {
            if (!giftMonths.Contains(current))
                missedMonths.Add(current);
            current = current.AddMonths(1);
        }
        
        return missedMonths;
    }
    
    private static string CleanDonorName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        
        return name.Trim()
                   .ToUpperInvariant()
                   .Replace(".", "")
                   .Replace(",", "")
                   .Replace("  ", " "); // Multiple spaces to single space
    }
    
    public bool CouldBeVariationOf(string otherName)
    {
        var cleanOther = CleanDonorName(otherName);
        var cleanThis = CleanDonorName(PrimaryName);
        
        // Exact match after cleaning (can enhance with fuzzy matching later)
        return cleanThis == cleanOther;
    }
    
    private static bool IsInDateRange(Donation donation, DateTime start, DateTime end)
    {
        return donation.Date.Date >= start.Date && donation.Date.Date <= end.Date;
    }

    // Summary statistics (based on complete history)
    public decimal TotalGiving => AllDonations.Sum(d => (decimal)d.Amount);
    public DateTime? FirstGiftDate => AllDonations.Any() ? AllDonations.Min(d => d.Date) : null;
    public DateTime? LastGiftDate => AllDonations.Any() ? AllDonations.Max(d => d.Date) : null;
    public int TotalGifts => AllDonations.Count;
    public decimal AverageGift => TotalGifts > 0 ? TotalGiving / TotalGifts : 0;
    public decimal ExpectedMonthlyAmount => CalculateExpectedMonthlyAmount();
    
}

public class DonorFrequencyResult
{
    public DonorFrequency Frequency { get; }
    public List<DateTime> MissedMonths { get; } = new();
    public FrequencyChangeResult FrequencyChange { get; }
    
    public bool HasMissedMonths => MissedMonths.Any();
    public bool HasFrequencyChanged => FrequencyChange.HasChanged;
    
    public DonorFrequencyResult(DonorFrequency frequency, FrequencyChangeResult? frequencyChange = null, List<DateTime>? missedMonths = null)
    {
        Frequency = frequency;
        FrequencyChange = frequencyChange ?? new FrequencyChangeResult(false, frequency, frequency);
        if (missedMonths != null)
            MissedMonths.AddRange(missedMonths);
    }
    
}

public class FrequencyChangeResult
{
    public bool HasChanged { get; }
    public DonorFrequency PreviousFrequency { get; }
    public DonorFrequency CurrentFrequency { get; }
    
    public FrequencyChangeResult(bool hasChanged, DonorFrequency previousFrequency, DonorFrequency currentFrequency)
    {
        HasChanged = hasChanged;
        PreviousFrequency = previousFrequency;
        CurrentFrequency = currentFrequency;
    }
}

