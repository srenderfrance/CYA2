using Cya2.Application.DTOs;
using Cya2.Application.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class FundReferenceCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_ReusesReferencesForSameGeneration()
    {
        var cache = new FundReferenceCache();
        var calls = 0;
        var references = new[] { new FundReferenceDto { AccountId = 1, Fund = "FUND-1" } };

        var first = await cache.GetOrCreateAsync(4, _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<FundReferenceDto>>(references);
        });
        var second = await cache.GetOrCreateAsync(4, _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<FundReferenceDto>>(references);
        });

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReloadsAfterGenerationChanges()
    {
        var cache = new FundReferenceCache();
        var calls = 0;

        await cache.GetOrCreateAsync(1, _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<FundReferenceDto>>([]);
        });
        await cache.GetOrCreateAsync(2, _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<FundReferenceDto>>([]);
        });

        Assert.Equal(2, calls);
    }

    [Fact]
    public void FilterForUser_UsesCachedAccessOnlyForFiltering()
    {
        var service = new FundReferenceService(null!, null!, null!, null!);
        var references = new[]
        {
            new FundReferenceDto { AccountId = 1, UserIds = new HashSet<int> { 7 } },
            new FundReferenceDto { AccountId = 2, UserIds = new HashSet<int> { 8 } }
        };

        var filtered = service.FilterForUser(references, 7, canAccessAllAccounts: false);
        var global = service.FilterForUser(references, 99, canAccessAllAccounts: true);

        Assert.Single(filtered);
        Assert.Equal(1, filtered[0].AccountId);
        Assert.Equal(2, global.Count);
    }
}
