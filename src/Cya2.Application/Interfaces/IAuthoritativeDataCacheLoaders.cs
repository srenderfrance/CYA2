using Cya2.Application.Models;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IDonationDataCacheLoader
{
    Task<DonationDataCacheEntry> LoadAsync(
        UserAccountContextAccount account,
        DateRange queryRange,
        DonationDataCacheKey key,
        CancellationToken cancellationToken = default);
}

public interface IAccountingDataCacheLoader
{
    Task<AccountingDataCacheEntry> LoadAsync(
        UserAccountContextAccount account,
        DateRange queryRange,
        AccountingDataCacheKey key,
        CancellationToken cancellationToken = default);
}
