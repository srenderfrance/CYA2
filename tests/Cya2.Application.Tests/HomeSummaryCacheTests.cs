using Cya2.Application.DTOs;
using Cya2.Application.Models;
using Cya2.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class HomeSummaryCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_ReusesEntryForSameAccountGenerationAndPeriod()
    {
        var cache = new HomeSummaryCache(NullLogger<HomeSummaryCache>.Instance);
        var key = new HomeSummaryCacheKey(1, "Fund", 3, 2026, 2);
        var calls = 0;

        var first = await cache.GetOrCreateAsync(key, _ =>
        {
            calls++;
            return Task.FromResult(new HomeSummaryCacheEntry
            {
                CurrentMonth = new FinancialSummaryDto { TotalDonations = 10 }
            });
        });
        var second = await cache.GetOrCreateAsync(key, _ =>
        {
            calls++;
            return Task.FromResult(new HomeSummaryCacheEntry());
        });

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReloadsAfterGenerationChanges()
    {
        var cache = new HomeSummaryCache(NullLogger<HomeSummaryCache>.Instance);
        var calls = 0;

        await cache.GetOrCreateAsync(new HomeSummaryCacheKey(1, "Fund", 3, 2026, 2), _ =>
        {
            calls++;
            return Task.FromResult(new HomeSummaryCacheEntry());
        });
        await cache.GetOrCreateAsync(new HomeSummaryCacheKey(1, "Fund", 4, 2026, 2), _ =>
        {
            calls++;
            return Task.FromResult(new HomeSummaryCacheEntry());
        });

        Assert.Equal(2, calls);
        Assert.Equal(2, cache.Count);
        cache.RemoveStaleGenerations(4);
        Assert.Equal(1, cache.Count);
    }
}
