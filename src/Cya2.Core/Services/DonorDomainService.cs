using Cya2.Core.Entities;
using Cya2.Core.ValueObjects;
using Cya2.Core.Interfaces;
using Cya2.Core.Enums;

namespace Cya2.Core.Services;

public class DonorDomainService
{
    private readonly IDonorRepository _donorRepository;
    private readonly IDonationRepository _donationRepository;

    public DonorDomainService(IDonorRepository donorRepository, IDonationRepository donationRepository)
    {
        _donorRepository = donorRepository;
        _donationRepository = donationRepository;
    }

    public async Task<List<DonorSummary>> GetDonorSummariesAsync(string accountFund, DateRange dateRange)
    {
        var donations = await _donationRepository.GetByAccountFundAsync(accountFund);
        var filteredDonations = donations.Where(d => dateRange.Contains(d.Date)).ToList();

        var donorSummaries = filteredDonations
            .Where(d => !string.IsNullOrWhiteSpace(d.AccountName)) // Fixed: Use AccountName instead of DonorName
            .GroupBy(d => d.AccountName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DonorSummary
            {
                Name = g.Key,
                TotalAmount = g.Sum(d => (decimal)d.Amount), // Fixed: Cast double to decimal
                DonationCount = g.Count(),
                LastDonationDate = g.Max(d => d.Date),
                Frequency = CalculateFrequency(g.Count()),
                PrimaryPaymentMethod = g.GroupBy(d => d.PaymentMethod)
                                       .OrderByDescending(pm => pm.Count())
                                       .First().Key
            })
            .OrderByDescending(ds => ds.TotalAmount)
            .ThenBy(ds => ds.Name)
            .ToList();

        return donorSummaries;
    }

    public async Task<DonorProfile?> GetDonorProfileAsync(string donorName, string accountFund)
    {
        var donor = await _donorRepository.GetByNameAsync(donorName);
        if (donor == null)
            return null;

        var donations = await _donationRepository.GetByDonorAndAccountAsync(donorName, accountFund);

        return new DonorProfile
        {
            Donor = donor,
            TotalDonations = (decimal)donations.Sum(d => d.Amount), // Fixed: Cast double to decimal
            DonationCount = donations.Count,
            FirstDonationDate = donations.Any() ? donations.Min(d => d.Date) : (DateTime?)null,
            LastDonationDate = donations.Any() ? donations.Max(d => d.Date) : (DateTime?)null,
            IsActive = donor.IsActive(DateTime.Today),
            Donations = donations.OrderByDescending(d => d.Date).ToList()
        };
    }

    private static DonorFrequency CalculateFrequency(int donationCount)
    {
        return donationCount switch
        {
            0 => DonorFrequency.None,
            1 => DonorFrequency.OneTime,
            <= 4 => DonorFrequency.Sporadic, // Fixed: Use new enum value
            _ => DonorFrequency.Monthly // Fixed: Use new enum value
        };
    }
}

public class DonorSummary
{
    public string Name { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int DonationCount { get; set; }
    public DateTime LastDonationDate { get; set; }
    public DonorFrequency Frequency { get; set; }
    public string PrimaryPaymentMethod { get; set; } = string.Empty; // Fixed: Use string instead of PaymentMethod enum
}

public class DonorProfile
{
    public Donor Donor { get; set; } = null!;
    public decimal TotalDonations { get; set; }
    public int DonationCount { get; set; }
    public DateTime? FirstDonationDate { get; set; }
    public DateTime? LastDonationDate { get; set; }
    public bool IsActive { get; set; }
    public List<Donation> Donations { get; set; } = new();
}