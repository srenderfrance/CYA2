using System;
using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

public class DonorSummaryDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneSummary { get; set; } = string.Empty;
    public string AddressSummary { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public DonorFrequency Frequency { get; set; } = DonorFrequency.None;
    /// <summary>
    /// True when this donor is a monthly giver with one or more missing gifts
    /// in the recent alert window.
    /// </summary>
    public bool HasMissingGiftAlert { get; set; }
    /// <summary>
    /// The year-month values (formatted "MMMM yyyy") of months with missing gifts.
    /// Empty when HasMissingGiftAlert is false.
    /// </summary>
    public List<string> MissingMonths { get; set; } = new();
}
