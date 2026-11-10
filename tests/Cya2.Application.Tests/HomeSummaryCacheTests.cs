using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class HomeSummaryCacheTests
{
    [Fact]
    public async Task WarmedInternSummary_IsReusedByDashboardWithAccountOverheadAndGeneration()
    {
        var now = DateTime.Now;
        var account = new Account { AccountId = 12, Fund = "Intern: Jane Doe", AccountNumber = "INT-12", Overhead = 12m };
        var contextAccount = new UserAccountContextAccount { AccountId = 12, Fund = account.Fund, InternName = "Jane Doe", Overhead = 12m };
        var context = new DashboardContext(contextAccount);
        var cache = new HomeSummaryCache(NullLogger<HomeSummaryCache>.Instance);
        var versions = new CacheInvalidationVersion();
        var calculations = new AccountCalculationService(new ExpenseClassificationService(), null!, null!);
        var service = new FinancialDashboardService(NullLogger<FinancialDashboardService>.Instance,
            calculations, null!, null!, null!, null!, null!, context, null!, new AllowDashboardAccess(), null!, versions, cache, null!);
        var data = new CacheRebuildData
        {
            DonationsByInternName = new Dictionary<string, List<DonationRecord>>(StringComparer.Ordinal)
            {
                ["Jane Doe"] = [new DonationRecord { Date = now.Date, Amount = 100d, Fund = "Shared", InternName = "Jane Doe" }]
            }
        };

        await service.WarmInternSummaryFromDataAsync(account, data);
        var dashboard = await service.GetDashboardSummaryDataAsync("Jane Doe", "12");

        Assert.True(dashboard.HasAccountData);
        Assert.Equal(100m, dashboard.CurrentMonth.TotalDonations);
        Assert.Equal(12m, dashboard.CurrentMonth.TotalOverhead);
        Assert.Equal(1, cache.Count);
        versions.Invalidate(Cya2.Core.Interfaces.CacheDataDomains.Accounting);
        dashboard = await service.GetDashboardSummaryDataAsync("Jane Doe", "12");
        Assert.Equal(100m, dashboard.CurrentMonth.TotalDonations);
        Assert.Equal(1, cache.Count);
    }

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
        cache.RemoveStaleGenerations(4, 4);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task RemoveStaleGenerations_PreservesCurrentInternEntriesSeparatelyFromNormalEntries()
    {
        var cache = new HomeSummaryCache(NullLogger<HomeSummaryCache>.Instance);
        var normal = new HomeSummaryCacheKey(1, "Fund", "", 7, 2026, 2);
        var intern = new HomeSummaryCacheKey(2, "", "Jane Doe", 3, 2026, 2);
        var staleIntern = new HomeSummaryCacheKey(3, "", "Other", 2, 2026, 2);
        foreach (var key in new[] { normal, intern, staleIntern })
            await cache.GetOrCreateAsync(key, _ => Task.FromResult(new HomeSummaryCacheEntry()));

        cache.RemoveStaleGenerations(7, 3);

        Assert.True(cache.TryGet(normal, out _));
        Assert.True(cache.TryGet(intern, out _));
        Assert.False(cache.TryGet(staleIntern, out _));
    }

    private sealed class DashboardContext(UserAccountContextAccount account) : IUserAccountContextService
    {
        public Task<UserAccountContext?> GetContextAsync(string userId, bool isAdminOrViewerHint = false) =>
            Task.FromResult<UserAccountContext?>(new UserAccountContext { IsIntern = true, InternName = account.InternName, Accounts = [account] });
        public UserAccountContextAccount? ResolveSelectedAccount(UserAccountContext context, string? preferredIdentity) => account;
        public void Invalidate(string userId) { }
    }

    private sealed class AllowDashboardAccess : IUserAuthorizationService
    {
        public Task<UserAuthorizationResult> ValidateUserAsync(string userIdentifier) => Allowed();
        public Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, int accountId) => Allowed();
        public Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, string accountFund) => Allowed();
        private static Task<UserAuthorizationResult> Allowed() => Task.FromResult(new UserAuthorizationResult(true, true, false, false, 12, string.Empty));
    }
}
