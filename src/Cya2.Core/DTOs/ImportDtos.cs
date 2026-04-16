using Cya2.Core.Enums;

namespace Cya2.Core.DTOs;

public sealed class DonationImportRowDto
{
    public required DateTime Date { get; init; }
    public required string AccountName { get; init; }
    public required string PaymentMethod { get; init; }
    public required string GiftType { get; init; }
    public required double Amount { get; init; }
    public required string Fund { get; init; }
    public string? SoftCreditName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Email { get; init; }
    public string? PhoneFixed { get; init; }
    public string? PhoneMobile { get; init; }
    public bool IsAnonymous { get; init; }
    public DateTime DateCreated { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Computed by DonorFrequencyService during import.
    /// Null means not yet classified.
    /// </summary>
    public DonorFrequency? Frequency { get; set; }
}

public sealed class AccountingImportRowDto
{
    public required string AccountingClass { get; init; }
    public required DateTime Date { get; init; }
    public required string Num { get; init; }
    public required double Amount { get; init; }
    public required string AccountNumber { get; init; }
    public required string Account { get; init; }
    public required string Type { get; init; }
    public DateTime DateCreated { get; init; } = DateTime.UtcNow;
}

public sealed class ImportBatchResult
{
    public int Inserted { get; set; }
    public List<string> Errors { get; } = new();
}
