using Cya2.Core.Enums;

namespace Cya2.Application.DTOs;

public class DonationDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string DonorName { get; set; } = string.Empty;
    public string AccountFund { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public GiftType GiftType { get; set; }
    public string? SoftCreditName { get; set; }
    public bool IsAnonymous { get; set; }
    public DateTime DateCreated { get; set; }
}