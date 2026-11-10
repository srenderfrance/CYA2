using System.Collections.Concurrent;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class CacheRebuildInternIsolationTests
{
    [Fact]
    public async Task OverviewFallback_SeparatesInternLoadsAndKeepsInternRows()
    {
        var accounts = new[]
        {
            new Account { AccountId = 1, Fund = "General", AccountNumber = "100" },
            new Account { AccountId = 2, Fund = "Intern: Jane Doe", AccountNumber = "INT-2" },
            new Account { AccountId = 3, Fund = "Intern: John Doe", AccountNumber = "INT-3" }
        };
        var versions = new CacheInvalidationVersion();
        var shared = new AdminSharedCache();
        shared.SetAccounts(versions.Current, accounts);
        shared.SetSubAccounts(versions.Current, []);
        var donations = new DonationReader();
        var accounting = new AccountingReader();
        var classification = new ExpenseClassificationService();
        var calculation = new AccountCalculationService(classification, new AccountingTransactionProcessor(classification), null!);
        var preload = new AdminPreloadService(null!, null!, calculation, shared, null!, new AccountBalanceCache(),
            new References(), versions, new NoWarmup(), donations, accounting, NullLogger<AdminPreloadService>.Instance);

        var overview = await preload.GetAccountOverviewAsync();

        Assert.Equal(new[] { 1 }, donations.NormalAccountIds);
        Assert.Equal(new[] { 1 }, accounting.AccountIds);
        Assert.Equal(10m, overview.Single(row => row.Fund == "Intern: Jane Doe").Last12MonthsDonations);
        Assert.Equal(20m, overview.Single(row => row.Fund == "Intern: John Doe").Last12MonthsDonations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rebuild_SeparatesInternLoadsAndWarmsTheirOwnFacts(bool warmSummaries)
    {
        var accounts = new[]
        {
            new Account { AccountId = 1, Fund = "General", AccountNumber = "100" },
            new Account { AccountId = 2, Fund = "Intern: Jane Doe", AccountNumber = "INT-2" },
            new Account { AccountId = 3, Fund = "Intern: John Doe", AccountNumber = "INT-3" }
        };
        var preload = new Preload(accounts);
        var donations = new DonationReader();
        var accounting = new AccountingReader();
        var dashboard = new Dashboard();
        var donors = new Donors();
        var rebuild = new CacheRebuildService(preload, dashboard, donors, donations, accounting, NullLogger<CacheRebuildService>.Instance);

        await rebuild.RebuildAsync(warmSummaries);

        Assert.Equal(new[] { 1 }, donations.NormalAccountIds);
        Assert.Equal(new[] { 1 }, accounting.AccountIds);
        Assert.Equal(new[] { "Jane Doe", "John Doe" }, donations.InternNames.Order(StringComparer.Ordinal));
        Assert.True(donations.InternStart <= DateTime.UtcNow.Date.AddMonths(-12));
        Assert.Equal(new DateTime(DateTime.Now.Year, 12, 31), donations.InternEnd);
        Assert.Equal(3, preload.Data!.Accounts.Count);
        if (warmSummaries)
        {
            Assert.Equal(new[] { 1 }, dashboard.NormalIds.Order());
            Assert.Equal(new[] { 2, 3 }, dashboard.InternIds.Order());
            Assert.Equal(10, donors.Amounts[2]);
            Assert.Equal(20, donors.Amounts[3]);
            Assert.Equal("Jane Doe", donors.Names[2]);
            Assert.Equal("John Doe", donors.Names[3]);
        }

        else
        {
            Assert.Empty(dashboard.InternIds);
            Assert.Empty(donors.Amounts);
        }
    }

    private sealed class References : IFundReferenceService
    {
        public Task<IReadOnlyList<FundReferenceDto>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FundReferenceDto>>([]);
        public IReadOnlyList<FundReferenceDto> FilterForUser(IReadOnlyList<FundReferenceDto> references, int userId, bool canAccessAllAccounts) => references;
    }

    private sealed class NoWarmup : IAdminCacheWarmupCoordinator
    {
        public void QueueWarmup(bool warmSummaries = true) { }
        public Task WaitForWarmupAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Preload(Account[] accounts) : IAdminPreloadService
    {
        public CacheRebuildData? Data { get; private set; }
        public Task<AdminPreloadState> PreloadMetadataAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AdminPreloadState { Accounts = accounts });
        public Task<IReadOnlyList<AdminAccountOverviewDto>> RebuildOverviewFromDataAsync(CacheRebuildData data, CancellationToken cancellationToken = default) { Data = data; return Task.FromResult<IReadOnlyList<AdminAccountOverviewDto>>([]); }
        public Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default) => PreloadMetadataAsync(cancellationToken);
        public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Account>>(accounts);
        public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SubAccount>>([]);
        public Task<IReadOnlyList<AdminUserDto>> GetStaffAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminUserDto>>([]);
        public Task<IReadOnlyList<AdminAccountOverviewDto>> GetAccountOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminAccountOverviewDto>>([]);
        public void Invalidate() { }
    }

    private sealed class DonationReader : IDonationReadRepository
    {
        public int[] NormalAccountIds { get; private set; } = [];
        public string[] InternNames { get; private set; } = [];
        public DateTime InternStart { get; private set; }
        public DateTime InternEnd { get; private set; }
        public Task<IReadOnlyDictionary<int, List<DonationRecord>>> GetDonationsForAccountsAsync(IReadOnlyList<(int AccountId, string Fund, IReadOnlyList<string> MergedSubFunds)> accounts, DateTime startDate, DateTime endDate)
        { NormalAccountIds = accounts.Select(a => a.AccountId).ToArray(); return Task.FromResult<IReadOnlyDictionary<int, List<DonationRecord>>>(new Dictionary<int, List<DonationRecord>>()); }
        public Task<IReadOnlyDictionary<string, List<DonationRecord>>> GetInternDonationsByNamesAndDateRangeAsync(IReadOnlyList<string> names, DateTime startDate, DateTime endDate)
        {
            InternNames = names.ToArray(); InternStart = startDate; InternEnd = endDate;
            return Task.FromResult<IReadOnlyDictionary<string, List<DonationRecord>>>(new Dictionary<string, List<DonationRecord>>(StringComparer.Ordinal)
            {
                ["Jane Doe"] = [new DonationRecord { Fund = "Shared", InternName = "Jane Doe", Amount = 10, Date = DateTime.Today }],
                ["John Doe"] = [new DonationRecord { Fund = "Shared", InternName = "John Doe", Amount = 20, Date = DateTime.Today }]
            });
        }
        public Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> funds) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int id, string fund) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> funds, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> funds, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int id, string fund, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int id, string fund, DateTime start, DateTime end) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> funds, string donor) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int id, string fund, string donor) => throw new NotImplementedException();
        public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> funds, string donor) => throw new NotImplementedException();
        public Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int id, string fund, string donor) => throw new NotImplementedException();
        public Task<List<DonationRecord>> GetInternDonationsByInternNameAndDateRangeAsync(string name, DateTime start, DateTime end) => throw new NotImplementedException();
    }

    private sealed class AccountingReader : IExpenseReadRepository
    {
        public int[] AccountIds { get; private set; } = [];
        public Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts, DateTime start, DateTime end)
        { AccountIds = accounts.Select(a => a.AccountId).ToArray(); return Task.FromResult<IReadOnlyDictionary<int, List<AccountingRecord>>>(new Dictionary<int, List<AccountingRecord>>()); }
        public Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(string accountingClass, string accountNumber, DateTime start, DateTime end) => throw new NotImplementedException();
    }

    private sealed class Dashboard : IFinancialDashboardService
    {
        public ConcurrentBag<int> NormalIds { get; } = [];
        public ConcurrentBag<int> InternIds { get; } = [];
        public Task WarmHomeSummaryFromDataAsync(Account account, CacheRebuildData data, CancellationToken cancellationToken = default) { NormalIds.Add(account.AccountId); return Task.CompletedTask; }
        public Task WarmInternSummaryFromDataAsync(Account account, CacheRebuildData data, CancellationToken cancellationToken = default) { InternIds.Add(account.AccountId); return Task.CompletedTask; }
        public Task<FinancialDashboardDto> GetDashboardDataAsync(string fund, string user) => throw new NotImplementedException();
        public Task<FinancialDashboardDto> GetDashboardSummaryDataAsync(string fund, string user) => throw new NotImplementedException();
        public Task<FinancialSummaryDto> GetCustomSummaryAsync(string fund, DateTime start, DateTime end, string user) => throw new NotImplementedException();
        public Task<List<UserAccountDto>> GetUserAccountsAsync(string user) => throw new NotImplementedException();
        public Task<bool> ValidateAccountAccessAsync(string fund, string user) => throw new NotImplementedException();
        public Task<List<MonthlyAccountVisualizationDto>> GetMonthlyVisualizationAsync(string fund, DateTime start, DateTime end, string user) => throw new NotImplementedException();
    }

    private sealed class Donors : IDonorService
    {
        public ConcurrentDictionary<int, double> Amounts { get; } = new();
        public ConcurrentDictionary<int, string> Names { get; } = new();
        public Task WarmMissingGiftDonorsFromDataAsync(AccountOptionDto account, IReadOnlyList<DonationRecord> donations, DateRange range, CancellationToken cancellationToken = default)
        { Amounts[account.AccountId] = donations.Sum(d => d.Amount); Names[account.AccountId] = account.InternName ?? ""; return Task.CompletedTask; }
        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string fund, DateRange range) => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetDonorSummariesAsync(IEnumerable<string> funds, DateRange range) => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(int id, string fund, DateRange range) => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(AccountOptionDto account, DateRange range, string userId = "") => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetDonorSummariesForSelectionAsync(AccountOptionDto account, string selection, DateRange? range, string userId = "") => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetMissingGiftDonorsAsync(AccountOptionDto account, DateRange range) => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(int id, string fund) => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(IEnumerable<string> funds) => throw new NotImplementedException();
        public Task<DonorDetailDto?> GetDonorDetailAsync(string donor, string fund) => throw new NotImplementedException();
        public Task<DonorDetailDto?> GetDonorDetailForAccountAsync(string donor, AccountOptionDto account) => throw new NotImplementedException();
        public Task<List<string>> GetDonorNamesAsync(string fund) => throw new NotImplementedException();
        public Task<List<SubAccount>> GetSubAccountsForAccountAsync(int id) => throw new NotImplementedException();
        public Task<string> FormatDonorContactForCopyAsync(string donor, string fund) => throw new NotImplementedException();
        public Task<List<DonorSummaryDto>> SearchDonorsAsync(string term, string fund) => throw new NotImplementedException();
        public Task UpdateDonorContactInfoAsync(string donor, string email, string mobile, string fixedPhone, string address, string city, string state, string postal, string country) => throw new NotImplementedException();
        public string? GetLastQuery() => null;
    }
}
