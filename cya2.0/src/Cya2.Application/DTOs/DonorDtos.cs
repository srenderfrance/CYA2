using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

public class DonorSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int DonationCount { get; set; }
    public string LastDonation { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneSummary { get; set; } = string.Empty;
    public string AddressSummary { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DonorFrequency Frequency { get; set; }
}

public class DonorDetailDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneMobile { get; set; } = string.Empty;
    public string PhoneFixed { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public decimal TotalDonations { get; set; }
    public DateTime FirstDonationDate { get; set; }
    public DateTime LastDonationDate { get; set; }
    public DonorFrequency Frequency { get; set; }
    public bool IsActive { get; set; }
    public List<DonationDto> RecentDonations { get; set; } = new();
}

public class DonationDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string DonorName { get; set; } = string.Empty;
    public string AccountFund { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string GiftType { get; set; } = string.Empty;
    public string? SoftCreditName { get; set; }
    public bool IsAnonymous { get; set; }
    public DateTime DateCreated { get; set; }
}