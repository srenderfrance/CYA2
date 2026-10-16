using Cya2.Application.Models;

namespace Cya2.Application.Interfaces;

public interface IDonationDataCache
{
    Task<DonationDataCacheEntry> GetOrCreateAsync(
        DonationDataCacheKey key,
        Func<CancellationToken, Task<DonationDataCacheEntry>> factory,
        CancellationToken cancellationToken = default);

    bool TryGet(DonationDataCacheKey key, out DonationDataCacheEntry entry);
    void Remove(DonationDataCacheKey key);
    void RemoveAccount(int accountId);
    void RemoveStaleGenerations(long currentDonationsGeneration);
    void InvalidateAll();
}

public interface IUserDonationDataCache
{
    bool TryGet(UserDonationDataCacheKey key, out UserDonationDataCacheEntry entry);
    bool TryGet(UserDonationDataCacheKey key, DateTime startDate, DateTime endDate, out UserDonationDataCacheEntry entry);
    Task<UserDonationDataCacheEntry> AddCoverageAsync(UserDonationDataCacheKey key, DateTime startDate, DateTime endDate, Func<CancellationToken, Task<UserDonationDataCacheEntry>> factory, CancellationToken cancellationToken = default);
    void RemoveUser(string userId);
    void RemoveStaleGenerations(long currentGeneration);
    void InvalidateAll();
}

public interface IUserAccountingDataCache
{
    bool TryGet(UserAccountingDataCacheKey key, out UserAccountingDataCacheEntry entry);
    bool TryGet(UserAccountingDataCacheKey key, DateTime startDate, DateTime endDate, out UserAccountingDataCacheEntry entry);
    Task<UserAccountingDataCacheEntry> AddCoverageAsync(UserAccountingDataCacheKey key, DateTime startDate, DateTime endDate, Func<CancellationToken, Task<UserAccountingDataCacheEntry>> factory, CancellationToken cancellationToken = default);
    void RemoveUser(string userId);
    void RemoveStaleGenerations(long currentGeneration);
    void InvalidateAll();
}

public interface IUserScopedCacheInvalidator
{
    void RemoveUser(string userId);
}

public interface IAccountingDataCache
{
    Task<AccountingDataCacheEntry> GetOrCreateAsync(
        AccountingDataCacheKey key,
        Func<CancellationToken, Task<AccountingDataCacheEntry>> factory,
        CancellationToken cancellationToken = default);

    bool TryGet(AccountingDataCacheKey key, out AccountingDataCacheEntry entry);
    void Remove(AccountingDataCacheKey key);
    void RemoveAccount(int accountId);
    void RemoveStaleGenerations(long currentAccountingGeneration);
    void InvalidateAll();
}
