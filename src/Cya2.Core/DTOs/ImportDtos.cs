using Cya2.Core.Enums;

namespace Cya2.Core.DTOs;

public sealed class DonationImportRowDto
{
    public int SourceRowNumber { get; init; }
    public required DateTime Date { get; init; }
    public required string AccountName { get; init; }
    public required string PaymentMethod { get; init; }
    public required string GiftType { get; init; }
    public required double Amount { get; init; }
    public required string Fund { get; init; }
    private string? _intern;
    private string? _primaryAddressee;
    private string? _softCreditName;
    private string? _honorMemorialName;
    private string? _address;
    private string? _city;
    private string? _state;
    private string? _postalCode;
    private string? _country;
    private string? _email;
    private string? _phoneFixed;
    private string? _phoneMobile;
    private string? _giftImportId;

    public string? Intern { get => IsAnonymous ? null : _intern; init => _intern = value; }
    public string? PrimaryAddressee { get => IsAnonymous ? null : _primaryAddressee; init => _primaryAddressee = value; }
    public string? Addressee
    {
        get => PrimaryAddressee;
        init => _primaryAddressee = value;
    }
    public string? SoftCreditName { get => IsAnonymous ? null : _softCreditName; init => _softCreditName = value; }
    public string? HonorMemorialName { get => IsAnonymous ? null : _honorMemorialName; init => _honorMemorialName = value; }
    public string? Address { get => IsAnonymous ? null : _address; set => _address = value; }
    public string? City { get => IsAnonymous ? null : _city; set => _city = value; }
    public string? State { get => IsAnonymous ? null : _state; set => _state = value; }
    public string? PostalCode { get => IsAnonymous ? null : _postalCode; set => _postalCode = value; }
    public string? Country { get => IsAnonymous ? null : _country; set => _country = value; }
    public string? Email { get => IsAnonymous ? null : _email; set => _email = value; }
    public string? PhoneFixed { get => IsAnonymous ? null : _phoneFixed; set => _phoneFixed = value; }
    public string? PhoneMobile { get => IsAnonymous ? null : _phoneMobile; set => _phoneMobile = value; }
    public string? GiftImportId { get => _giftImportId; init => _giftImportId = value; }
    public string? ResolvedDonorName { get; set; }
    public string? DonorIdentityKey { get; set; }
    public string? DonorResolutionSource { get; set; }
    public int DonorResolutionVersion { get; set; }
    public Dictionary<string, List<string>> ObservedContacts { get; } = new(StringComparer.OrdinalIgnoreCase);
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
