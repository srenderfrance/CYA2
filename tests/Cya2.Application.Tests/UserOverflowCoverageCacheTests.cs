using Cya2.Application.Models;
using Cya2.Application.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class UserOverflowCoverageCacheTests
{
    [Fact]
    public async Task DonationCache_CallerCancellationDoesNotCancelSharedLoad()
    {
        var cache = new UserDonationDataCache();
        var key = new UserDonationDataCacheKey("user", 1, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryToken = CancellationToken.None;

        var first = cache.AddCoverageAsync(key, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31), async token =>
        {
            factoryToken = token;
            started.SetResult();
            await release.Task;
            return new UserDonationDataCacheEntry(key, [], [], [], DateTime.UtcNow);
        });

        await started.Task;
        using var callerCancellation = new CancellationTokenSource();
        var second = cache.AddCoverageAsync(key, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31), _ =>
            Task.FromException<UserDonationDataCacheEntry>(new InvalidOperationException("A second factory was started.")), callerCancellation.Token);
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(factoryToken.IsCancellationRequested);

        release.SetResult();
        await first;
        Assert.True(cache.TryGet(key, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31), out _));
    }

    [Fact]
    public async Task DonationCache_InvalidationCancelsLoadAndPreventsStaleInsertion()
    {
        var cache = new UserDonationDataCache();
        var key = new UserDonationDataCacheKey("user", 1, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryToken = CancellationToken.None;

        var load = cache.AddCoverageAsync(key, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31), async token =>
        {
            factoryToken = token;
            started.SetResult();
            await release.Task;
            return new UserDonationDataCacheEntry(key, [], [], [], DateTime.UtcNow);
        });

        await started.Task;
        cache.RemoveUser("user");
        Assert.True(factoryToken.IsCancellationRequested);
        release.SetResult();
        await load;

        Assert.False(cache.TryGet(key, out _));
    }

    [Fact]
    public async Task AccountingCache_InvalidationPreventsStaleInsertion()
    {
        var cache = new UserAccountingDataCache();
        var key = new UserAccountingDataCacheKey("user", 1, "class", "number", 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var load = cache.AddCoverageAsync(key, new DateTime(2024, 1, 1), new DateTime(2024, 1, 31), async _ =>
        {
            started.SetResult();
            await release.Task;
            return new UserAccountingDataCacheEntry(key, [], [], DateTime.UtcNow);
        });

        await started.Task;
        cache.InvalidateAll();
        release.SetResult();
        await load;

        Assert.False(cache.TryGet(key, out _));
    }
}
