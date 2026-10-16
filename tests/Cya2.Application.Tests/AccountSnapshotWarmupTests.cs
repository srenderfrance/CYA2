using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class AccountSnapshotWarmupTests
{
    [Fact]
    public async Task WarmSelectedAccountAsync_ExistingSnapshot_WarmsDerivedCaches()
    {
        var account = new UserAccountContextAccount
        {
            AccountId = 7,
            Fund = "FUND-7",
            AccountingClass = "CLASS-7",
            AccountNumber = "ACCOUNT-7"
        };
        var donationCache = new TrackingDonationCache();
        var accountingCache = new TrackingAccountingCache();
        var dashboard = new TrackingDashboardService();
        var donations = new TrackingDonationService();
        var expenses = new TrackingExpenseService();
        var donors = new TrackingDonorService();

        using var warmup = new AccountSnapshotWarmupService(
            donationCache,
            new NoOpDonationLoader(),
            accountingCache,
            new NoOpAccountingLoader(),
            new CacheInvalidationVersion(),
            dashboard,
            donations,
            expenses,
            donors,
            NullLogger<AccountSnapshotWarmupService>.Instance);

        await warmup.WarmSelectedAccountAsync(account, "user-7");

        Assert.Equal(1, dashboard.SummaryCalls);
        Assert.Equal(1, donations.LoadCalls);
        Assert.Equal(1, expenses.LoadCalls);
        Assert.Equal(1, donors.SummaryCalls);
    }

    [Fact]
    public async Task WarmInitialAccountsAsync_WarmsDefaultAndFourNonDefaultAccounts()
    {
        var accounts = Enumerable.Range(1, 5)
            .Select(id => new UserAccountContextAccount
            {
                AccountId = id,
                Fund = $"FUND-{id}",
                AccountingClass = $"CLASS-{id}",
                AccountNumber = $"ACCOUNT-{id}"
            })
            .ToList();
        var donationCache = new TrackingDonationCache();
        var donationLoader = new TrackingDonationLoader();
        var accountingCache = new TrackingAccountingCache();
        var accountingLoader = new TrackingAccountingLoader();
        var donors = new TrackingDonorService();

        using var warmup = new AccountSnapshotWarmupService(
            donationCache,
            donationLoader,
            accountingCache,
            accountingLoader,
            new CacheInvalidationVersion(),
            new TrackingDashboardService(),
            new TrackingDonationService(),
            new TrackingExpenseService(),
            donors,
            NullLogger<AccountSnapshotWarmupService>.Instance);

        await warmup.WarmInitialAccountsAsync(accounts, defaultAccountId: 1, userId: "user-1");
        await donationLoader.AllLoadsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(5, donationLoader.LoadedAccountIds.Count);
        Assert.Equal(accounts.Select(account => account.AccountId).ToHashSet(), donationLoader.LoadedAccountIds.ToHashSet());
        Assert.Equal(5, donors.SummaryCalls);
        Assert.Equal(5, donors.MissingGiftCalls);
    }

    [Fact]
    public async Task WarmInitialAccountsAsync_AdminUser_WarmsOnlyDefaultAccount()
    {
        var accounts = Enumerable.Range(1, 5)
            .Select(id => new UserAccountContextAccount
            {
                AccountId = id,
                Fund = $"FUND-{id}",
                AccountingClass = $"CLASS-{id}",
                AccountNumber = $"ACCOUNT-{id}"
            })
            .ToList();
        var donationCache = new TrackingDonationCache();
        var donationLoader = new TrackingDonationLoader();
        var accountingCache = new TrackingAccountingCache();
        var accountingLoader = new TrackingAccountingLoader();
        var donors = new TrackingDonorService();

        using var warmup = new AccountSnapshotWarmupService(
            donationCache,
            donationLoader,
            accountingCache,
            accountingLoader,
            new CacheInvalidationVersion(),
            new TrackingDashboardService(),
            new TrackingDonationService(),
            new TrackingExpenseService(),
            donors,
            NullLogger<AccountSnapshotWarmupService>.Instance);

        await warmup.WarmInitialAccountsAsync(accounts, defaultAccountId: 1, userId: "admin-1", isAdminUser: true);
        await Task.Delay(100);

        Assert.Single(donationLoader.LoadedAccountIds);
        Assert.Equal(1, donationLoader.LoadedAccountIds.Single());
        Assert.Equal(1, donors.SummaryCalls);
        Assert.Equal(1, donors.MissingGiftCalls);
    }

    [Fact]
    public async Task WarmInitialAccountsAsync_PassesWarningRangeToEveryAccountWarmup()
    {
        var accounts = Enumerable.Range(1, 5)
            .Select(id => new UserAccountContextAccount
            {
                AccountId = id,
                Fund = $"FUND-{id}",
                AccountingClass = $"CLASS-{id}",
                AccountNumber = $"ACCOUNT-{id}"
            })
            .ToList();
        var donationLoader = new TrackingDonationLoader();
        var accountingLoader = new TrackingAccountingLoader();
        var donors = new TrackingDonorService();
        var warningRange = new DateRange(new DateTime(2026, 2, 28), new DateTime(2026, 8, 31));

        using var warmup = new AccountSnapshotWarmupService(
            new TrackingDonationCache(),
            donationLoader,
            new TrackingAccountingCache(),
            accountingLoader,
            new CacheInvalidationVersion(),
            new TrackingDashboardService(),
            new TrackingDonationService(),
            new TrackingExpenseService(),
            donors,
            NullLogger<AccountSnapshotWarmupService>.Instance);

        await warmup.WarmInitialAccountsAsync(accounts, defaultAccountId: 1, userId: "user-1", donorSummaryRange: warningRange);
        await donationLoader.AllLoadsCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);

        Assert.Equal(5, donors.WarmedWarningRanges.Count);
        Assert.All(donors.WarmedWarningRanges, range => Assert.Equal(warningRange, range));
    }

    [Fact]
    public async Task WarmSelectedAccountAsync_ConcurrentRequests_JoinTheExistingWarmup()
    {
        var account = new UserAccountContextAccount
        {
            AccountId = 8,
            Fund = "FUND-8",
            AccountingClass = "CLASS-8",
            AccountNumber = "ACCOUNT-8"
        };
        var donationCache = new TrackingDonationCache();
        var accountingCache = new TrackingAccountingCache();
        var dashboard = new TrackingDashboardService { BlockSummary = true };
        var donations = new TrackingDonationService();
        var expenses = new TrackingExpenseService();
        var donors = new TrackingDonorService();

        using var warmup = new AccountSnapshotWarmupService(
            donationCache,
            new NoOpDonationLoader(),
            accountingCache,
            new NoOpAccountingLoader(),
            new CacheInvalidationVersion(),
            dashboard,
            donations,
            expenses,
            donors,
            NullLogger<AccountSnapshotWarmupService>.Instance);

        var first = warmup.WarmSelectedAccountAsync(account, "user-8");
        await dashboard.SummaryStarted.Task;
        var second = warmup.WarmSelectedAccountAsync(account, "user-8");
        dashboard.ReleaseSummary();

        await Task.WhenAll(first, second);

        Assert.Equal(1, dashboard.SummaryCalls);
        Assert.Equal(1, donations.LoadCalls);
        Assert.Equal(1, expenses.LoadCalls);
        Assert.Equal(1, donors.SummaryCalls);
    }

    private sealed class TrackingDonationCache : IDonationDataCache
    {
        private readonly Dictionary<DonationDataCacheKey, DonationDataCacheEntry> _entries = [];
        public async Task<DonationDataCacheEntry> GetOrCreateAsync(DonationDataCacheKey key, Func<CancellationToken, Task<DonationDataCacheEntry>> factory, CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(key, out var entry)) return entry;
            entry = await factory(cancellationToken);
            _entries[key] = entry;
            return entry;
        }
        public bool TryGet(DonationDataCacheKey key, out DonationDataCacheEntry entry) => _entries.TryGetValue(key, out entry!);
        public void Remove(DonationDataCacheKey key) => _entries.Remove(key);
        public void RemoveAccount(int accountId) { foreach (var key in _entries.Keys.Where(key => key.AccountId == accountId).ToList()) _entries.Remove(key); }
        public void RemoveStaleGenerations(long generation) { foreach (var key in _entries.Keys.Where(key => key.DonationsGeneration != generation).ToList()) _entries.Remove(key); }
        public void InvalidateAll() => _entries.Clear();
    }

    private sealed class TrackingAccountingCache : IAccountingDataCache
    {
        private readonly Dictionary<AccountingDataCacheKey, AccountingDataCacheEntry> _entries = [];
        public async Task<AccountingDataCacheEntry> GetOrCreateAsync(AccountingDataCacheKey key, Func<CancellationToken, Task<AccountingDataCacheEntry>> factory, CancellationToken cancellationToken = default) { if (_entries.TryGetValue(key, out var entry)) return entry; entry = await factory(cancellationToken); _entries[key] = entry; return entry; }
        public bool TryGet(AccountingDataCacheKey key, out AccountingDataCacheEntry entry) => _entries.TryGetValue(key, out entry!);
        public void Remove(AccountingDataCacheKey key) => _entries.Remove(key);
        public void RemoveAccount(int accountId) { foreach (var key in _entries.Keys.Where(key => key.AccountId == accountId).ToList()) _entries.Remove(key); }
        public void RemoveStaleGenerations(long generation) { foreach (var key in _entries.Keys.Where(key => key.AccountingGeneration != generation).ToList()) _entries.Remove(key); }
        public void InvalidateAll() => _entries.Clear();
    }

    private sealed class NoOpDonationLoader : IDonationDataCacheLoader
    {
        public Task<DonationDataCacheEntry> LoadAsync(UserAccountContextAccount account, DateRange queryRange, DonationDataCacheKey key, CancellationToken cancellationToken = default) => Task.FromResult(new DonationDataCacheEntry(key, [], [], DateTime.UtcNow));
    }

    private sealed class NoOpAccountingLoader : IAccountingDataCacheLoader
    {
        public Task<AccountingDataCacheEntry> LoadAsync(UserAccountContextAccount account, DateRange queryRange, AccountingDataCacheKey key, CancellationToken cancellationToken = default) => Task.FromResult(new AccountingDataCacheEntry(key, [], DateTime.UtcNow));
    }

    private sealed class TrackingDonationLoader : IDonationDataCacheLoader
    {
        public List<int> LoadedAccountIds { get; } = [];
        public TaskCompletionSource<bool> AllLoadsCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DonationDataCacheEntry> LoadAsync(UserAccountContextAccount account, DateRange queryRange, DonationDataCacheKey key, CancellationToken cancellationToken = default)
        {
            lock (LoadedAccountIds)
            {
                LoadedAccountIds.Add(account.AccountId);
                if (LoadedAccountIds.Count == 5)
                {
                    AllLoadsCompleted.TrySetResult(true);
                }
            }

            return Task.FromResult(new DonationDataCacheEntry(key, [], [], DateTime.UtcNow));
        }
    }

    private sealed class TrackingAccountingLoader : IAccountingDataCacheLoader
    {
        public Task<AccountingDataCacheEntry> LoadAsync(UserAccountContextAccount account, DateRange queryRange, AccountingDataCacheKey key, CancellationToken cancellationToken = default) => Task.FromResult(new AccountingDataCacheEntry(key, [], DateTime.UtcNow));
    }

    private sealed class TrackingDashboardService : IFinancialDashboardService
    {
        public int SummaryCalls { get; private set; }
        public bool BlockSummary { get; set; }
        public TaskCompletionSource<bool> SummaryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseSummary = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FinancialDashboardDto> GetDashboardSummaryDataAsync(string accountFund, string userId)
        {
            SummaryCalls++;
            SummaryStarted.TrySetResult(true);
            if (BlockSummary)
            {
                await _releaseSummary.Task;
            }

            return new FinancialDashboardDto();
        }
        public void ReleaseSummary() => _releaseSummary.TrySetResult(true);
        public Task<FinancialDashboardDto> GetDashboardDataAsync(string accountFund, string userId) => Task.FromResult(new FinancialDashboardDto());
        public Task<List<UserAccountDto>> GetUserAccountsAsync(string userId) => Task.FromResult(new List<UserAccountDto>());
        public Task<bool> ValidateAccountAccessAsync(string accountFund, string userId) => Task.FromResult(true);
        public Task<FinancialSummaryDto> GetCustomSummaryAsync(string accountFund, DateTime startDate, DateTime endDate, string userId) => Task.FromResult(new FinancialSummaryDto());
        public Task<List<MonthlyAccountVisualizationDto>> GetMonthlyVisualizationAsync(string accountFund, DateTime startDate, DateTime endDate, string userId) => Task.FromResult(new List<MonthlyAccountVisualizationDto>());
    }

    private sealed class TrackingDonationService : IDonationService
    {
        public int LoadCalls { get; private set; }
        public Task<DonationDataDto> GetDonationDataAsync(string accountName, string? subAccountSelection, DateRange dateRange, string userId, bool isAdminOrViewer = false, bool forceRefresh = false)
        {
            LoadCalls++;
            return Task.FromResult(new DonationDataDto());
        }
    }

    private sealed class TrackingExpenseService : IExpenseService
    {
        public int LoadCalls { get; private set; }
        public Task<ExpenseDataDto> GetExpenseDataAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false) { LoadCalls++; return Task.FromResult(new ExpenseDataDto()); }
        public Task<List<AccountOptionDto>> GetUserAccountsAsync(string userId, bool isAdminOrViewer = false) => Task.FromResult(new List<AccountOptionDto>());
        public Task<List<ExpenseTransactionDto>> GetExpenseTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false) => Task.FromResult(new List<ExpenseTransactionDto>());
        public Task<List<ExpenseTransactionDto>> GetTransferTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false) => Task.FromResult(new List<ExpenseTransactionDto>());
        public Task<ExpenseSummaryDto> GetExpenseSummaryAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false) => Task.FromResult(new ExpenseSummaryDto());
    }

    private sealed class TrackingDonorService : IDonorService
    {
        public int SummaryCalls { get; private set; }
        public int MissingGiftCalls { get; private set; }
        public List<DateRange> WarmedWarningRanges { get; } = [];
        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(AccountOptionDto account, DateRange dateRange, string userId = "") { SummaryCalls++; return Task.FromResult(new List<DonorSummaryDto>()); }
        public Task<List<DonorSummaryDto>> GetDonorSummariesForSelectionAsync(AccountOptionDto account, string selectedSubAccount, DateRange? dateRange, string userId = "") => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(IEnumerable<string> fundNames, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(int accountId, string accountFund, DateRange dateRange) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetMissingGiftDonorsAsync(AccountOptionDto account, DateRange dateRange)
        {
            MissingGiftCalls++;
            lock (WarmedWarningRanges)
            {
                WarmedWarningRanges.Add(dateRange);
            }

            return Task.FromResult(new List<DonorSummaryDto>());
        }
        public Task WarmMissingGiftDonorsFromDataAsync(AccountOptionDto account, IReadOnlyList<DonationRecord> donations, DateRange dateRange, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(int accountId, string accountFund) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(IEnumerable<string> fundNames) => Task.FromResult(new List<DonorSummaryDto>());
        public Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund) => Task.FromResult<DonorDetailDto?>(null);
        public Task<DonorDetailDto?> GetDonorDetailForAccountAsync(string donorName, AccountOptionDto account) => Task.FromResult<DonorDetailDto?>(null);
        public Task<List<string>> GetDonorNamesAsync(string accountFund) => Task.FromResult(new List<string>());
        public Task<List<SubAccount>> GetSubAccountsForAccountAsync(int accountId) => Task.FromResult(new List<SubAccount>());
        public Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund) => Task.FromResult(string.Empty);
        public Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund) => Task.FromResult(new List<DonorSummaryDto>());
        public Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed, string address, string city, string state, string postal, string country) => Task.CompletedTask;
        public string? GetLastQuery() => null;
    }
}
