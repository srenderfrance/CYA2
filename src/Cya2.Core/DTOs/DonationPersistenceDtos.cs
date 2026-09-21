using Cya2.Core.Enums;

namespace Cya2.Core.DTOs;

public sealed class DonorWriteModel
{
    public required string Fund { get; init; }
    public required string DisplayName { get; init; }
    public required string IdentityKey { get; init; }
    public required string ResolutionSource { get; init; }
    public required int ResolutionVersion { get; init; }
}

public sealed class DonorContactWriteModel
{
    public required string ContactType { get; init; }
    public required string ContactValue { get; init; }
    public required string NormalizedValue { get; init; }
}

public sealed class CanonicalDonationWriteModel
{
    public required DonorWriteModel Donor { get; init; }
    public required DateTime Date { get; init; }
    public string? GiftImportId { get; init; }
    public required string AccountName { get; init; }
    public required string PaymentMethod { get; init; }
    public required string GiftType { get; init; }
    public required decimal Amount { get; init; }
    public required string Fund { get; init; }
    public string? Intern { get; init; }
    public string? PrimaryAddressee { get; init; }
    public string? SoftCreditName { get; init; }
    public string? HonorMemorialName { get; init; }
    public bool IsAnonymous { get; init; }
    public DonorFrequency? Frequency { get; init; }
    public IReadOnlyList<DonorContactWriteModel> Contacts { get; init; } = Array.Empty<DonorContactWriteModel>();
}

public sealed class DonationImportReportEntry
{
    public required int CanonicalRowNumber { get; init; }
    public required IReadOnlyList<int> DuplicateRowNumbers { get; init; }
    public required string GiftImportId { get; init; }
    public required string Fund { get; init; }
    public required decimal Amount { get; init; }
    public required string DonorName { get; init; }
    public required string Reason { get; init; }
}

public sealed class DonationImportPersistenceResult
{
    public int Inserted { get; init; }
    public int ContactsAdded { get; init; }
}
