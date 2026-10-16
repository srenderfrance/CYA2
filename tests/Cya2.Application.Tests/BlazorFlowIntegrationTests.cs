using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Interfaces;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class BlazorFlowIntegrationTests
{
    [Fact]
    public void AdminSelection_IsAvailableToSubsequentPageFlowForSameUser()
    {
        var selection = new InMemoryUserSelectionStore();
        const string userId = "admin-42";

        selection.SetSelectedAccount(userId, "FUND-A");

        Assert.True(selection.TryGetSelectedAccount(userId, out var selectedFund));
        Assert.Equal("FUND-A", selectedFund);
    }

    [Fact]
    public void AdminSharedCache_IsolatedByGenerationAndCopiesValues()
    {
        var cache = new AdminSharedCache();
        var staff = new[] { new AdminUserDto() };

        cache.SetStaff(4, staff);
        staff[0] = new AdminUserDto();

        Assert.True(cache.TryGetStaff(4, out var cachedStaff));
        Assert.Single(cachedStaff);
        Assert.False(cache.TryGetStaff(5, out _));

        cache.InvalidateAll();

        Assert.False(cache.TryGetStaff(4, out _));
    }

    [Fact]
    public void AdminSharedCache_BoundsOverviewEntries()
    {
        var cache = new AdminSharedCache();
        var start = new DateTime(2025, 1, 1);

        for (var month = 0; month < 5; month++)
        {
            var rangeStart = start.AddMonths(month);
            cache.SetOverview(1, rangeStart, rangeStart.AddMonths(12), Array.Empty<AdminAccountOverviewDto>());
        }

        Assert.False(cache.TryGetOverview(1, start, start.AddMonths(12), out _));
        Assert.True(cache.TryGetOverview(1, start.AddMonths(4), start.AddMonths(16), out _));
    }

    [Fact]
    public void AccountBalanceCache_IsolatedByGenerationAndNormalizesFutureDates()
    {
        var cache = new AccountBalanceCache();
        var today = DateTime.UtcNow.Date;

        cache.Set(42, "CLASS", "NUMBER", 10m, 3, today.AddDays(30), "test-user", 123.45m);

        Assert.True(cache.TryGet(42, " CLASS ", "NUMBER", 10m, 3, today, "test-user", out var balance));
        Assert.Equal(123.45m, balance);
        Assert.False(cache.TryGet(42, "CLASS", "NUMBER", 10m, 4, today, "test-user", out _));
    }

    [Fact]
    public void AccountBalanceCache_RemovesOnlyStaleGenerations()
    {
        var cache = new AccountBalanceCache();
        var date = new DateTime(2026, 1, 1);

        cache.Set(1, "CLASS", "1", 0m, 1, date, "test-user", 10m);
        cache.Set(2, "CLASS", "2", 0m, 2, date, "test-user", 20m);
        cache.RemoveStaleGeneration(2);

        Assert.False(cache.TryGet(1, "CLASS", "1", 0m, 1, date, "test-user", out _));
        Assert.True(cache.TryGet(2, "CLASS", "2", 0m, 2, date, "test-user", out var balance));
        Assert.Equal(20m, balance);
    }

    [Fact]
    public void AccountBalanceCache_EvictsLeastRecentlyUsedEntries()
    {
        var cache = new AccountBalanceCache();
        var date = new DateTime(2026, 1, 1);

        for (var accountId = 1; accountId <= 257; accountId++)
        {
            cache.Set(accountId, "CLASS", accountId.ToString(), 0m, 1, date, "test-user", accountId);
        }

        Assert.Equal(256, cache.Count);
        Assert.False(cache.TryGet(1, "CLASS", "1", 0m, 1, date, "test-user", out _));
        Assert.True(cache.TryGet(257, "CLASS", "257", 0m, 1, date, "test-user", out _));
    }

    [Fact]
    public void AccountMetadataCache_IsolatedByGenerationAndClearsOnInvalidation()
    {
        var cache = new AccountMetadataCache();
        var account = new Cya2.Core.Entities.Account
        {
            AccountId = 42,
            Fund = "FUND-A",
            AccountingClass = "CLASS",
            AccountNumber = "1000"
        };
        var metadata = new Cya2.Application.Models.AccountMetadataSnapshot(
            account,
            Array.Empty<Cya2.Core.Entities.SubAccount>());

        cache.Set(account.AccountId, 3, metadata);

        Assert.True(cache.TryGet(account.AccountId, 3, out var cached));
        Assert.Same(metadata, cached);
        Assert.False(cache.TryGet(account.AccountId, 4, out _));

        cache.InvalidateAll();

        Assert.False(cache.TryGet(account.AccountId, 3, out _));
    }

    [Fact]
    public void CacheInvalidationVersion_AdvancesOnlyRequestedDomain()
    {
        var version = new CacheInvalidationVersion();
        var initialDonations = version.Donations;
        var initialAccounting = version.Accounting;
        var initialContacts = version.DonorContacts;

        version.Invalidate(CacheDataDomains.Donations);

        Assert.Equal(initialDonations + 1, version.Donations);
        Assert.Equal(initialAccounting, version.Accounting);
        Assert.Equal(initialContacts, version.DonorContacts);
    }

    private sealed class TrackingMissingGiftCache : IMissingGiftCache
    {
        public int InvalidationCount { get; private set; }
        public bool TryGet(int accountId, string fund, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration, out List<DonorSummaryDto> data) { data = []; return false; }
        public void Set(int accountId, string fund, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration, List<DonorSummaryDto> data) { }
        public void InvalidateAll() => InvalidationCount++;
    }

    private sealed class InMemoryUserSelectionStore
    {
        private readonly Dictionary<string, string> _selections = new(StringComparer.OrdinalIgnoreCase);

        public void SetSelectedAccount(string userId, string account, TimeSpan? ttl = null) => _selections[userId] = account;
        public bool TryGetSelectedAccount(string userId, out string account) => _selections.TryGetValue(userId, out account!);
    }

    private sealed class TrackingDonorCache : IDonorSummaryCache
    {
        public int InvalidationCount { get; private set; }
        public bool TryGet(string scope, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration, out List<DonorSummaryDto> data) { data = []; return false; }
        public void Set(string scope, DateTime startDate, DateTime endDate, long donationsGeneration, long donorContactsGeneration, long authorizationGeneration, List<DonorSummaryDto> data) { }
        public void InvalidateAll() => InvalidationCount++;
    }

}
