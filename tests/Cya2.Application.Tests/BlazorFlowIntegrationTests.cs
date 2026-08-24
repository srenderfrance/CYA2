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
    public void CentralInvalidator_InvalidatesEverySharedCacheAndAdvancesGeneration()
    {
        var donation = new TrackingDonationCache();
        var donor = new TrackingDonorCache();
        var expense = new TrackingExpenseCache();
        var dashboard = new TrackingDashboardCache();
        var snapshots = new TrackingSnapshotCache();
        var version = new CacheInvalidationVersion();
        var invalidator = new ImportCacheInvalidator(
            donation,
            donor,
            expense,
            dashboard,
            snapshots,
            version,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ImportCacheInvalidator>.Instance);

        var initialVersion = version.Current;
        invalidator.InvalidateAll();

        Assert.Equal(1, donation.InvalidationCount);
        Assert.Equal(1, donor.InvalidationCount);
        Assert.Equal(1, expense.InvalidationCount);
        Assert.Equal(1, dashboard.InvalidationCount);
        Assert.Equal(1, snapshots.InvalidationCount);
        Assert.Equal(initialVersion + 1, version.Current);
    }

    private sealed class InMemoryUserSelectionStore
    {
        private readonly Dictionary<string, string> _selections = new(StringComparer.OrdinalIgnoreCase);

        public void SetSelectedAccount(string userId, string account, TimeSpan? ttl = null) => _selections[userId] = account;
        public bool TryGetSelectedAccount(string userId, out string account) => _selections.TryGetValue(userId, out account!);
    }

    private sealed class TrackingDonationCache : ISessionDonationDataCacheService
    {
        public int InvalidationCount { get; private set; }
        public bool TryGetDonationData(string userId, string fund, out DonationDataDto data) { data = null!; return false; }
        public void SetDonationData(string userId, string fund, DonationDataDto data, bool prioritize = false) { }
        public IReadOnlyCollection<string> GetFunds(string userId) => Array.Empty<string>();
        public void InvalidateAll() => InvalidationCount++;
    }

    private sealed class TrackingDonorCache : ISessionDonorSummaryCacheService
    {
        public int InvalidationCount { get; private set; }
        public bool TryGetDonorSummaries(string fundsSignature, DateTime startDate, DateTime endDate, out List<DonorSummaryDto> data) { data = new(); return false; }
        public void SetDonorSummaries(string fundsSignature, DateTime startDate, DateTime endDate, List<DonorSummaryDto> data) { }
        public void InvalidateAll() => InvalidationCount++;
    }

    private sealed class TrackingExpenseCache : ISessionExpenseDataCacheService
    {
        public int InvalidationCount { get; private set; }
        public bool TryGetExpenseData(string userId, string fund, DateTime startDate, DateTime endDate, out ExpenseDataDto data) { data = null!; return false; }
        public void SetExpenseData(string userId, string fund, DateTime startDate, DateTime endDate, ExpenseDataDto data) { }
        public void InvalidateAll() => InvalidationCount++;
    }

    private sealed class TrackingDashboardCache : ISessionDashboardDtoCacheService
    {
        public int InvalidationCount { get; private set; }
        public bool TryGetDashboard(string userId, string fund, out FinancialDashboardDto dashboard) { dashboard = null!; return false; }
        public IReadOnlyCollection<string> GetFunds(string userId) => Array.Empty<string>();
        public void SetDashboard(string userId, string fund, FinancialDashboardDto dashboard, bool prioritize = false) { }
        public void InvalidateAll() => InvalidationCount++;
    }

    private sealed class TrackingSnapshotCache : IAccountSnapshotCache
    {
        public int InvalidationCount { get; private set; }
        public int Count => 0;
        public Task<Cya2.Application.Models.AccountDataSnapshot> GetOrCreateAsync(Cya2.Application.Models.AccountSnapshotKey key, Func<CancellationToken, Task<Cya2.Application.Models.AccountDataSnapshot>> factory, CancellationToken cancellationToken = default) => factory(cancellationToken);
        public bool TryGet(Cya2.Application.Models.AccountSnapshotKey key, out Cya2.Application.Models.AccountDataSnapshot snapshot) { snapshot = null!; return false; }
        public void InvalidateAll() => InvalidationCount++;
    }
}
