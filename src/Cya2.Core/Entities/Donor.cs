using Cya2.Core.Enums;

namespace Cya2.Core.Entities;

public class Donor : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? PhoneMobile { get; private set; }
    public string? PhoneFixed { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string? Country { get; private set; }
    public List<Donation> Donations { get; private set; } = new();

    // Private constructor for EF Core
    private Donor() { }

    public Donor(string name)
    {
        // No validation - just like current system
        Name = name?.Trim() ?? string.Empty;
    }

    public void UpdateContactInfo(string? email, string? phoneMobile, string? phoneFixed,
                                 string? address, string? city, string? state, 
                                 string? postalCode, string? country)
    {
        // No validation - just update values like current system
        Email = email?.Trim();
        PhoneMobile = phoneMobile?.Trim();
        PhoneFixed = phoneFixed?.Trim();
        Address = address?.Trim();
        City = city?.Trim();
        State = state?.Trim();
        PostalCode = postalCode?.Trim();
        Country = country?.Trim() ?? "US";
        SetModified();
    }

    public void AddDonation(Donation donation)
    {
        if (donation != null && !Donations.Contains(donation))
        {
            Donations.Add(donation);
            SetModified();
        }
    }

    public void RemoveDonation(Donation donation)
    {
        if (donation != null && Donations.Remove(donation))
        {
            SetModified();
        }
    }

    public double GetTotalDonations(DateTime? startDate = null, DateTime? endDate = null)
    {
        var donations = Donations.AsQueryable();
        
        if (startDate.HasValue)
            donations = donations.Where(d => d.Date >= startDate.Value);
            
        if (endDate.HasValue)
            donations = donations.Where(d => d.Date <= endDate.Value);
            
        return donations.Sum(d => d.Amount);
    }

    public int GetDonationCount(DateTime? startDate = null, DateTime? endDate = null)
    {
        var donations = Donations.AsQueryable();
        
        if (startDate.HasValue)
            donations = donations.Where(d => d.Date >= startDate.Value);
            
        if (endDate.HasValue)
            donations = donations.Where(d => d.Date <= endDate.Value);
            
        return donations.Count();
    }

    public DateTime? GetLastDonationDate()
    {
        return Donations.Any() ? Donations.Max(d => d.Date) : null;
    }

    public DateTime? GetFirstDonationDate()
    {
        return Donations.Any() ? Donations.Min(d => d.Date) : null;
    }

    public bool IsActive(DateTime asOfDate, int monthsBack = 24)
    {
        var cutoffDate = asOfDate.AddMonths(-monthsBack);
        return Donations.Any(d => d.Date >= cutoffDate);
    }

    public DonorFrequency CalculateFrequency(DateTime startDate, DateTime endDate)
    {
        var count = GetDonationCount(startDate, endDate);
        return count switch
        {
            0 => DonorFrequency.None,
            1 => DonorFrequency.OneTime,
            <= 4 => DonorFrequency.Sporadic, // Fixed: Use new enum value
            _ => DonorFrequency.Monthly // Fixed: Use new enum value
        };
    }
}