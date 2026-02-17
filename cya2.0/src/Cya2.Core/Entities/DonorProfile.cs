using Cya2.Core.Enums;
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
    public DonorFrequencyResult GetFrequencyAnalysis(DateTime userStartDate, DateTime userEndDate)
    {
        if (!AllDonations.Any())
            return new DonorFrequencyResult(DonorFrequency.None);
            
        // Use COMPLETE giving history for frequency calculation
        var firstGift = AllDonations.Min(d => d.Date);
        var lastGift = AllDonations.Max(d => d.Date);
        
        // Calculate frequency based on complete history
        var currentFrequency = CalculateFrequencyFromCompleteHistory(firstGift, lastGift);
        
        // Check if frequency has changed by comparing recent vs. historical patterns
        var frequencyChange = DetectFrequencyChange(userStartDate, userEndDate);
        
        // Get missed months (accounting for catch-up donations)
        var missedMonths = currentFrequency == DonorFrequency.Monthly 
            ? GetActualMissedMonths(firstGift, lastGift) 
            : new List<DateTime>();
        
        return new DonorFrequencyResult(currentFrequency, frequencyChange, missedMonths);
    }
    
    private DonorFrequency CalculateFrequencyFromCompleteHistory(DateTime firstGift, DateTime lastGift)
    {
        var totalGifts = AllDonations.Count;
        
        if (totalGifts == 1)
            return DonorFrequency.OneTime;
            
        // Monthly check - requires minimum consecutive months WITH catch-up logic
        if (IsMonthlyDonorWithCatchUp(firstGift, lastGift))
        {
            return DonorFrequency.Monthly;
        }
        
        // Yearly check - one gift per year around same time
        if (IsYearlyDonor(firstGift, lastGift))
        {
            return DonorFrequency.Yearly;
        }
        
        // Multiple gifts but irregular
        return DonorFrequency.Sporadic;
    }
    
    private bool IsMonthlyDonorWithCatchUp(DateTime firstGift, DateTime lastGift)
    {
        var totalGifts = AllDonations.Count;
        
        // Require minimum 3 gifts to be considered monthly
        if (totalGifts < 3)
            return false;
        
        // Calculate expected monthly amount from regular donations
        var expectedMonthlyAmount = CalculateExpectedMonthlyAmount();
        if (expectedMonthlyAmount <= 0)
            return false;
        
        // Create virtual monthly coverage including catch-up donations
        var virtualMonthlyGifts = CreateVirtualMonthlyGifts(expectedMonthlyAmount, firstGift, lastGift);
        
        // Must have at least 2 consecutive months to start (including virtual)
        if (!HasConsecutiveMonthlyGifts(virtualMonthlyGifts, 2))
            return false;
            
        var totalPossibleMonths = GetMonthSpan(firstGift, lastGift);
        var coveredMonths = virtualMonthlyGifts.Count;
        
        // Consider monthly if they cover 70%+ of possible months (including catch-ups)
        return coveredMonths >= Math.Max(3, totalPossibleMonths * 0.7);
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
            .Where(d => d.IsInDateRange(start, end))
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
    
    private bool IsYearlyDonor(DateTime firstGift, DateTime lastGift)
    {
        var totalGifts = AllDonations.Count;
        var yearSpan = lastGift.Year - firstGift.Year + 1;
        
        // Must span multiple years and have reasonable gift count
        if (yearSpan < 2 || totalGifts < 2)
            return false;
            
        // Group gifts by year
        var giftsByYear = AllDonations
            .GroupBy(d => d.Date.Year)
            .OrderBy(g => g.Key)
            .ToList();
            
        // Should have gifts in most years (allowing for some gaps)
        var yearsWithGifts = giftsByYear.Count;
        if (yearsWithGifts < Math.Max(2, yearSpan * 0.6))
            return false;
            
        // Check if gifts occur around the same time each year (within +/- 1 month)
        return HasConsistentYearlyTiming(giftsByYear);
    }
    
    private bool HasConsistentYearlyTiming(List<IGrouping<int, Donation>> giftsByYear)
    {
        if (giftsByYear.Count < 2) return false;
        
        // Find the most common gift month across all years
        var allGiftMonths = giftsByYear
            .SelectMany(g => g.Select(d => d.Date.Month))
            .ToList();
            
        var mostCommonMonth = allGiftMonths
            .GroupBy(m => m)
            .OrderByDescending(g => g.Count())
            .First().Key;
        
        // Check if most years have gifts within +/- 1 month of the common month
        var consistentYears = giftsByYear.Count(yearGroup =>
        {
            return yearGroup.Any(d => Math.Abs(d.Date.Month - mostCommonMonth) <= 1 ||
                                    Math.Abs(d.Date.Month - mostCommonMonth) >= 11); // Handle Dec/Jan wrap
        });
        
        return consistentYears >= Math.Max(2, giftsByYear.Count * 0.7);
    }
    
    private FrequencyChangeResult DetectFrequencyChange(DateTime userStartDate, DateTime userEndDate)
    {
        // Compare recent pattern (user's date range) vs historical pattern
        var recentDonations = AllDonations.Where(d => d.IsInDateRange(userStartDate, userEndDate)).ToList();
        var historicalDonations = AllDonations.Where(d => d.Date < userStartDate).ToList();
        
        if (!historicalDonations.Any() || !recentDonations.Any())
            return new FrequencyChangeResult(false, DonorFrequency.None, DonorFrequency.None);
            
        var historicalStart = historicalDonations.Min(d => d.Date);
        var historicalEnd = historicalDonations.Max(d => d.Date);
        
        // Create temporary profiles for comparison
        var historicalProfile = CreateFromDonations("temp", historicalDonations);
        var recentProfile = CreateFromDonations("temp", recentDonations);
        
        var historicalFreq = historicalProfile.CalculateFrequencyFromCompleteHistory(historicalStart, historicalEnd);
        var recentFreq = recentProfile.CalculateFrequencyFromCompleteHistory(userStartDate, userEndDate);
        
        var hasChanged = historicalFreq != recentFreq && 
                        historicalFreq != DonorFrequency.None && 
                        recentFreq != DonorFrequency.None;
        
        return new FrequencyChangeResult(hasChanged, historicalFreq, recentFreq);
    }
    
    // Helper methods
    private List<DateTime> GetMonthlyGiftPattern(DateTime start, DateTime end)
    {
        return AllDonations
            .Where(d => d.IsInDateRange(start, end))
            .GroupBy(d => new { d.Date.Year, d.Date.Month })
            .Select(g => new DateTime(g.Key.Year, g.Key.Month, 1))
            .OrderBy(d => d)
            .ToList();
    }
    
    private bool HasConsecutiveMonthlyGifts(List<DateTime> monthlyGifts, int minConsecutive)
    {
        if (monthlyGifts.Count < minConsecutive) return false;
        
        var sortedGifts = monthlyGifts.OrderBy(d => d).ToList();
        
        for (int i = 0; i <= sortedGifts.Count - minConsecutive; i++)
        {
            bool hasConsecutive = true;
            for (int j = 1; j < minConsecutive; j++)
            {
                if (sortedGifts[i + j] != sortedGifts[i].AddMonths(j))
                {
                    hasConsecutive = false;
                    break;
                }
            }
            if (hasConsecutive) return true;
        }
        
        return false;
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
    
    private int GetMonthSpan(DateTime start, DateTime end)
    {
        return (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
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
    
    // Summary statistics (based on complete history)
    public decimal TotalGiving => AllDonations.Sum(d => (decimal)d.Amount);
    public DateTime? FirstGiftDate => AllDonations.Any() ? AllDonations.Min(d => d.Date) : null;
    public DateTime? LastGiftDate => AllDonations.Any() ? AllDonations.Max(d => d.Date) : null;
    public int TotalGifts => AllDonations.Count;
    public decimal AverageGift => TotalGifts > 0 ? TotalGiving / TotalGifts : 0;
    public decimal ExpectedMonthlyAmount => CalculateExpectedMonthlyAmount();
    
    // Get donations within user's selected range for display
    public List<Donation> GetDonationsInRange(DateTime start, DateTime end)
    {
        return AllDonations.Where(d => d.IsInDateRange(start, end)).OrderByDescending(d => d.Date).ToList();
    }
    
    public decimal GetGivingInRange(DateTime start, DateTime end)
    {
        return (decimal)AllDonations.Where(d => d.IsInDateRange(start, end)).Sum(d => d.Amount);
    }
    
    // Analyze specific donation for catch-up behavior
    public CatchUpAnalysis AnalyzeCatchUpDonation(Donation donation)
    {
        var expectedAmount = CalculateExpectedMonthlyAmount();
        if (expectedAmount <= 0) return new CatchUpAnalysis(false, 0);
        
        var donationAmount = (decimal)donation.Amount;
        var monthsCovered = Math.Round(donationAmount / expectedAmount);
        var tolerance = 0.1m; // 10% tolerance
        
        var isCatchUp = monthsCovered > 1 && 
                       monthsCovered <= 12 && 
                       Math.Abs(donationAmount - (monthsCovered * expectedAmount)) <= (expectedAmount * tolerance);
        
        return new CatchUpAnalysis(isCatchUp, (int)monthsCovered);
    }
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
    
    public string GetAlertMessage()
    {
        var alerts = new List<string>();
        
        if (HasMissedMonths)
        {
            var monthNames = MissedMonths.Take(3).Select(m => m.ToString("MMM yyyy"));
            var message = $"Monthly donor missed: {string.Join(", ", monthNames)}";
            if (MissedMonths.Count > 3)
                message += $" and {MissedMonths.Count - 3} more";
            alerts.Add(message);
        }
        
        if (HasFrequencyChanged)
        {
            alerts.Add($"Frequency changed: {FrequencyChange.PreviousFrequency} → {FrequencyChange.CurrentFrequency}");
        }
        
        return string.Join(" | ", alerts);
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

public class CatchUpAnalysis
{
    public bool IsCatchUp { get; }
    public int MonthsCovered { get; }
    
    public CatchUpAnalysis(bool isCatchUp, int monthsCovered)
    {
        IsCatchUp = isCatchUp;
        MonthsCovered = monthsCovered;
    }
    
    public string GetDisplayMessage()
    {
        return IsCatchUp ? $"Catch-up donation covering {MonthsCovered} months" : "Regular donation";
    }
}