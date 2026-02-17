using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

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
    public DateTime? LastDonationDate { get; set; }
    public DateTime? FirstDonationDate { get; set; }
    public DonorFrequency Frequency { get; set; }
    public bool IsActive { get; set; }
    public List<DonationDto> RecentDonations { get; set; } = new();
}