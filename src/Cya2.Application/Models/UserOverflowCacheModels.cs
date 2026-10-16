namespace Cya2.Application.Models;

public readonly record struct UserDonationDataCacheKey(
    string UserId,
    int AccountId,
    long DonationsGeneration)
{
    public UserDonationDataCacheKey Normalize() => this with { UserId = (UserId ?? string.Empty).Trim() };
}

public readonly record struct UserAccountingDataCacheKey(
    string UserId,
    int AccountId,
    string AccountingClass,
    string AccountNumber,
    long AccountingGeneration)
{
    public UserAccountingDataCacheKey Normalize() => this with
    {
        UserId = (UserId ?? string.Empty).Trim(),
        AccountingClass = (AccountingClass ?? string.Empty).Trim(),
        AccountNumber = (AccountNumber ?? string.Empty).Trim()
    };
}

public sealed record UserFactCoverage(DateTime StartDate, DateTime EndDate)
{
    public UserFactCoverage Normalize() => new(StartDate.Date, EndDate.Date);
    public bool Contains(DateTime startDate, DateTime endDate)
        => StartDate.Date <= startDate.Date && EndDate.Date >= endDate.Date;
}

public sealed class UserDonationDataCacheEntry
{
    public UserDonationDataCacheEntry(UserDonationDataCacheKey key, IEnumerable<UserFactCoverage> coverage, IEnumerable<DonationCacheRecord> donations, IEnumerable<SubAccountCacheRecord> subAccounts, DateTime createdUtc)
    {
        Key = key.Normalize();
        Coverage = (coverage ?? []).Select(c => c.Normalize()).ToArray();
        Donations = (donations ?? []).ToArray();
        SubAccounts = (subAccounts ?? []).ToArray();
        CreatedUtc = createdUtc;
    }

    public UserDonationDataCacheKey Key { get; }
    public IReadOnlyList<UserFactCoverage> Coverage { get; }
    public IReadOnlyList<DonationCacheRecord> Donations { get; }
    public IReadOnlyList<SubAccountCacheRecord> SubAccounts { get; }
    public DateTime CreatedUtc { get; }
}

public sealed class UserAccountingDataCacheEntry
{
    public UserAccountingDataCacheEntry(UserAccountingDataCacheKey key, IEnumerable<UserFactCoverage> coverage, IEnumerable<AccountingCacheRecord> transactions, DateTime createdUtc)
    {
        Key = key.Normalize();
        Coverage = (coverage ?? []).Select(c => c.Normalize()).ToArray();
        Transactions = (transactions ?? []).ToArray();
        CreatedUtc = createdUtc;
    }

    public UserAccountingDataCacheKey Key { get; }
    public IReadOnlyList<UserFactCoverage> Coverage { get; }
    public IReadOnlyList<AccountingCacheRecord> Transactions { get; }
    public DateTime CreatedUtc { get; }
}
