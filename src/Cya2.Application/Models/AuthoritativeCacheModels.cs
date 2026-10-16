using Cya2.Core.Enums;

namespace Cya2.Application.Models;

public readonly record struct DonationDataCacheKey(
    int AccountId,
    long DonationsGeneration,
    DateTime WindowStart,
    DateTime WindowEnd)
{
    public DonationDataCacheKey Normalize() => this with { WindowStart = WindowStart.Date, WindowEnd = WindowEnd.Date };
}

public readonly record struct AccountingDataCacheKey(
    int AccountId,
    string AccountingClass,
    string AccountNumber,
    long AccountingGeneration,
    DateTime WindowStart,
    DateTime WindowEnd)
{
    public AccountingDataCacheKey Normalize() => this with
    {
        AccountingClass = (AccountingClass ?? string.Empty).Trim(),
        AccountNumber = (AccountNumber ?? string.Empty).Trim(),
        WindowStart = WindowStart.Date,
        WindowEnd = WindowEnd.Date
    };
}

public sealed class DonationDataCacheEntry
{
    public DonationDataCacheEntry(
        DonationDataCacheKey key,
        IEnumerable<DonationCacheRecord> donations,
        IEnumerable<SubAccountCacheRecord> subAccounts,
        DateTime createdUtc)
    {
        Key = key.Normalize();
        Donations = (donations ?? []).ToArray();
        SubAccounts = (subAccounts ?? []).ToArray();
        CreatedUtc = createdUtc;
    }

    public DonationDataCacheKey Key { get; }
    public IReadOnlyList<DonationCacheRecord> Donations { get; }
    public IReadOnlyList<SubAccountCacheRecord> SubAccounts { get; }
    public DateTime CreatedUtc { get; }
}

public sealed record DonationCacheRecord(
    int Id,
    long DonorId,
    string DonorDisplayName,
    DateTime Date,
    double Amount,
    DonorFrequency? Frequency,
    string Fund,
    string AccountName,
    string? SoftCreditName,
    string? Addressee,
    bool IsAnonymous);

public sealed record SubAccountCacheRecord(
    int Id,
    int AccountId,
    string SubFund,
    string Kind);

public sealed class AccountingDataCacheEntry
{
    public AccountingDataCacheEntry(
        AccountingDataCacheKey key,
        IEnumerable<AccountingCacheRecord> transactions,
        DateTime createdUtc)
    {
        Key = key.Normalize();
        Transactions = (transactions ?? []).ToArray();
        CreatedUtc = createdUtc;
    }

    public AccountingDataCacheKey Key { get; }
    public IReadOnlyList<AccountingCacheRecord> Transactions { get; }
    public DateTime CreatedUtc { get; }
}

public sealed record AccountingCacheRecord(
    int Id,
    DateTime Date,
    string Number,
    double Amount,
    string Account,
    string Type,
    string AccountingClass,
    string AccountNumber);
