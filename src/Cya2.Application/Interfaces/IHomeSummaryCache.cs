using Cya2.Application.Models;

namespace Cya2.Application.Interfaces;

public interface IHomeSummaryCache
{
    Task<HomeSummaryCacheEntry> GetOrCreateAsync(
        HomeSummaryCacheKey key,
        Func<CancellationToken, Task<HomeSummaryCacheEntry>> factory,
        CancellationToken cancellationToken = default);

    bool TryGet(HomeSummaryCacheKey key, out HomeSummaryCacheEntry entry);
    void RemoveStaleGenerations(long generation);
    void InvalidateAll();
    int Count { get; }
}
