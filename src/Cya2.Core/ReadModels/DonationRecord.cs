using Cya2.Core.Enums;

namespace Cya2.Core.ReadModels;

public class DonationRecord
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    /// <summary>
    /// Frequency classification stored at DB write time.
    /// Null means not yet classified (legacy records or pre-pipeline records).
    /// </summary>
    public DonorFrequency? Frequency { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string GiftType { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string Fund { get; set; } = string.Empty;
    public string? SoftCreditName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? PhoneFixed { get; set; }
    public string? PhoneMobile { get; set; }
    public DateTime DateCreated { get; set; }
    public bool IsAnonymous { get; set; }
}
