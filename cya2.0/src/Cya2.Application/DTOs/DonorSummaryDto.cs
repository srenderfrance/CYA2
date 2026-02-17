using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

public class DonorSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string LastDonation { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneSummary { get; set; } = string.Empty;
    public string AddressSummary { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DonorFrequency Frequency { get; set; }
    public int DonationCount { get; set; }
    public bool IsActive { get; set; }
}