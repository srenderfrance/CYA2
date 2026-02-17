using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Cya2.Core.Enums;

namespace Cya2.Application.Services;

public class DonorService : IDonorService
{
    private readonly IDonorRepository _donorRepository;
    private readonly IDonationRepository _donationRepository;

    public DonorService(IDonorRepository donorRepository, IDonationRepository donationRepository)
    {
        _donorRepository = donorRepository;
        _donationRepository = donationRepository;
    }

    public async Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange)
    {
        var donations = await _donationRepository.GetByAccountFundAsync(accountFund);
        var filteredDonations = donations.Where(d => dateRange.Contains(d.Date)).ToList();

        var donorSummaries = filteredDonations
            .Where(d => !string.IsNullOrWhiteSpace(d.AccountName)) // Fixed: Use AccountName instead of DonorName
            .GroupBy(d => d.AccountName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DonorSummaryDto
            {
                Name = g.Key,
                Total = (decimal)g.Sum(d => d.Amount), // Fixed: Cast to decimal
                DonationCount = g.Count(),
                LastDonation = FormatDate(g.Max(d => d.Date)),
                Email = GetFirstNonEmpty(g.Select(d => GetDonationEmail(d))),
                PhoneSummary = GetPhoneSummary(g.ToList()),
                AddressSummary = GetFirstNonEmpty(g.Select(d => GetDonationAddress(d))),
                PaymentMethod = g.GroupBy(d => d.PaymentMethod)
                               .OrderByDescending(pm => pm.Count())
                               .First().Key,
                Frequency = CalculateFrequency(g.Count())
            })
            .OrderByDescending(ds => ds.Total)
            .ThenBy(ds => ds.Name)
            .ToList();

        return donorSummaries;
    }

    public async Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund)
    {
        var donations = await _donationRepository.GetByDonorAndAccountAsync(donorName, accountFund);
        if (!donations.Any())
            return null;

        var donor = await _donorRepository.GetByNameAsync(donorName);
        var orderedDonations = donations.OrderByDescending(d => d.Date).ToList();

        return new DonorDetailDto
        {
            Name = donorName,
            Email = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationEmail(d))),
            PhoneMobile = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationPhoneMobile(d))),
            PhoneFixed = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationPhoneFixed(d))),
            Address = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationAddress(d))),
            City = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationCity(d))),
            State = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationState(d))),
            PostalCode = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationPostalCode(d))),
            Country = GetFirstNonEmpty(orderedDonations.Select(d => GetDonationCountry(d))),
            TotalDonations = (decimal)donations.Sum(d => d.Amount), // Fixed: Cast to decimal
            FirstDonationDate = donations.Min(d => d.Date),
            LastDonationDate = donations.Max(d => d.Date),
            Frequency = CalculateFrequency(donations.Count),
            IsActive = donor?.IsActive(DateTime.Today) ?? false,
            RecentDonations = orderedDonations.Take(10).Select(d => new DonationDto
            {
                Id = d.Id,
                Amount = (decimal)d.Amount, // Fixed: Cast to decimal
                Date = d.Date,
                DonorName = d.AccountName, // Fixed: Use AccountName
                AccountFund = d.Fund, // Fixed: Use Fund
                PaymentMethod = d.PaymentMethod,
                GiftType = d.GiftType,
                SoftCreditName = d.SoftCreditName,
                IsAnonymous = d.IsAnonymous,
                DateCreated = d.DateCreated
            }).ToList()
        };
    }

    public async Task<List<string>> GetDonorNamesAsync(string accountFund)
    {
        var donations = await _donationRepository.GetByAccountFundAsync(accountFund);
        return donations
            .Where(d => !string.IsNullOrWhiteSpace(d.AccountName)) // Fixed: Use AccountName
            .Select(d => d.AccountName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
    }

    public async Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund)
    {
        var donorDetail = await GetDonorDetailAsync(donorName, accountFund);
        if (donorDetail == null)
            return string.Empty;

        var lines = new List<string>
        {
            $"Name: {donorDetail.Name}",
            $"Email: {(string.IsNullOrWhiteSpace(donorDetail.Email) ? "None" : donorDetail.Email)}"
        };

        // Add phone information
        var phoneLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(donorDetail.PhoneMobile))
            phoneLines.Add($"Cell: {FormatPhone(donorDetail.PhoneMobile)}");
        if (!string.IsNullOrWhiteSpace(donorDetail.PhoneFixed))
            phoneLines.Add($"Primary: {FormatPhone(donorDetail.PhoneFixed)}");

        if (phoneLines.Any())
            lines.Add($"Phone: {string.Join("; ", phoneLines)}");
        else
            lines.Add("Phone: None");

        // Add address information
        if (!string.IsNullOrWhiteSpace(donorDetail.Address))
        {
            lines.Add("Address:");
            lines.Add($"  {donorDetail.Address}");
            var cityStatePostal = string.Join(" ", new[] { donorDetail.City, donorDetail.State, donorDetail.PostalCode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(cityStatePostal))
                lines.Add($"  {cityStatePostal}");
            if (!string.IsNullOrWhiteSpace(donorDetail.Country))
                lines.Add($"  {donorDetail.Country}");
        }
        else
        {
            lines.Add("Address: None");
        }

        return string.Join("\n", lines);
    }

    public async Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return new List<DonorSummaryDto>();

        var donorNames = await GetDonorNamesAsync(accountFund);
        var matchingNames = donorNames
            .Where(name => name.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        var summaries = new List<DonorSummaryDto>();
        foreach (var name in matchingNames)
        {
            var donations = await _donationRepository.GetByDonorAndAccountAsync(name, accountFund);
            if (donations.Any())
            {
                summaries.Add(new DonorSummaryDto
                {
                    Name = name,
                    Total = (decimal)donations.Sum(d => d.Amount), // Fixed: Cast to decimal
                    DonationCount = donations.Count,
                    LastDonation = FormatDate(donations.Max(d => d.Date)),
                    Frequency = CalculateFrequency(donations.Count)
                });
            }
        }

        return summaries.OrderByDescending(s => s.Total).ToList();
    }

    public async Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed,
                                                string address, string city, string state, string postal, string country)
    {
        var donor = await _donorRepository.GetByNameAsync(donorName);
        if (donor == null)
        {
            donor = new Donor(donorName);
            donor = await _donorRepository.AddAsync(donor);
        }

        donor.UpdateContactInfo(email, phoneMobile, phoneFixed, address, city, state, postal, country);
        await _donorRepository.UpdateAsync(donor);
    }

    // Helper methods
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

    private static string FormatDate(DateTime date)
    {
        return date.ToString("MM/dd/yyyy");
    }

    private static string FormatPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            10 => $"({digits[..3]}) {digits[3..6]}-{digits[6..]}",
            11 when digits[0] == '1' => $"1-({digits[1..4]}) {digits[4..7]}-{digits[7..]}",
            _ => phone
        };
    }

    private static string GetFirstNonEmpty(IEnumerable<string> values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }

    private static string GetPhoneSummary(List<Donation> donations)
    {
        var phones = new List<string>();
        
        var mobile = GetFirstNonEmpty(donations.Select(d => GetDonationPhoneMobile(d)));
        if (!string.IsNullOrWhiteSpace(mobile))
            phones.Add($"Cell: {FormatPhone(mobile)}");
            
        var phoneFixed = GetFirstNonEmpty(donations.Select(d => GetDonationPhoneFixed(d)));
        if (!string.IsNullOrWhiteSpace(phoneFixed))
            phones.Add($"Primary: {FormatPhone(phoneFixed)}");
            
        return string.Join("; ", phones);
    }

    // Donation property accessors - using actual Donation entity properties
    private static string GetDonationEmail(Donation donation) => donation.Email ?? string.Empty;
    private static string GetDonationPhoneMobile(Donation donation) => donation.PhoneMobile ?? string.Empty;
    private static string GetDonationPhoneFixed(Donation donation) => donation.PhoneFixed ?? string.Empty;
    private static string GetDonationAddress(Donation donation) => donation.Address ?? string.Empty;
    private static string GetDonationCity(Donation donation) => donation.City ?? string.Empty;
    private static string GetDonationState(Donation donation) => donation.State ?? string.Empty;
    private static string GetDonationPostalCode(Donation donation) => donation.PostalCode ?? string.Empty;
    private static string GetDonationCountry(Donation donation) => donation.Country ?? string.Empty;
}