using Cya2.Application.Interfaces;
using Cya2.Application.DTOs;
using Cya2.Application.Models;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class CacheInvalidationTests
{
    [Fact]
    public async Task UserAccountContextService_ReloadsAccountMetadataAfterInvalidation()
    {
        var version = new CacheInvalidationVersion();
        var user = new User
        {
            Id = 901,
            Email = $"cache-test-{Guid.NewGuid():N}@example.test",
            AuthLevel = "User"
        };
        var account = new Account
        {
            AccountId = 42,
            Fund = "TEST",
            AccountingClass = "CLASS",
            AccountNumber = "1000",
            BalanceAdjustment = 10m
        };
        var users = new FakeUserRepository(user);
        var accounts = new FakeAccountRepository(account);
        var access = new FakeUserAccountAccessRepository(account);
        var service = new UserAccountContextService(
            users,
            access,
            accounts,
            version,
            NullLogger<UserAccountContextService>.Instance);

        var first = await service.GetContextAsync(user.Id.ToString());
        account.BalanceAdjustment = 25m;

        var cached = await service.GetContextAsync(user.Id.ToString());
        version.Invalidate();
        var refreshed = await service.GetContextAsync(user.Id.ToString());

        Assert.NotNull(first);
        Assert.NotNull(cached);
        Assert.NotNull(refreshed);
        Assert.Equal(10m, first!.Accounts.Single().BalanceAdjustment);
        Assert.Equal(10m, cached!.Accounts.Single().BalanceAdjustment);
        Assert.Equal(25m, refreshed!.Accounts.Single().BalanceAdjustment);
        Assert.Equal(2, access.GetUserAccountsCallCount);
    }

    [Fact]
    public async Task UserAccountContextService_DonationAndAccountingChangesReuseContext()
    {
        var version = new CacheInvalidationVersion();
        var user = new User { Id = 902, Email = $"cache-test-{Guid.NewGuid():N}@example.test", AuthLevel = "User" };
        var account = new Account
        {
            AccountId = 43,
            Fund = "TEST-DOMAINS",
            AccountingClass = "CLASS",
            AccountNumber = "1001"
        };
        var users = new FakeUserRepository(user);
        var access = new FakeUserAccountAccessRepository(account);
        var service = new UserAccountContextService(
            users,
            access,
            new FakeAccountRepository(account),
            version,
            NullLogger<UserAccountContextService>.Instance);

        await service.GetContextAsync(user.Id.ToString());
        version.Invalidate(CacheDataDomains.Donations);
        await service.GetContextAsync(user.Id.ToString());
        version.Invalidate(CacheDataDomains.Accounting);
        await service.GetContextAsync(user.Id.ToString());

        Assert.Equal(1, access.GetUserAccountsCallCount);
    }

    [Fact]
    public async Task UserAccountContextService_ReloadsContextAfterMetadataOrAuthorizationInvalidation()
    {
        var version = new CacheInvalidationVersion();
        var user = new User { Id = 903, Email = $"cache-test-{Guid.NewGuid():N}@example.test", AuthLevel = "User" };
        var account = new Account { AccountId = 44, Fund = "TEST-RELOAD", AccountingClass = "CLASS", AccountNumber = "1002" };
        var access = new FakeUserAccountAccessRepository(account);
        var service = new UserAccountContextService(
            new FakeUserRepository(user),
            access,
            new FakeAccountRepository(account),
            version,
            NullLogger<UserAccountContextService>.Instance);

        await service.GetContextAsync(user.Id.ToString());
        version.Invalidate(CacheDataDomains.AccountMetadata);
        await service.GetContextAsync(user.Id.ToString());
        version.Invalidate(CacheDataDomains.Authorization);
        await service.GetContextAsync(user.Id.ToString());

        Assert.Equal(3, access.GetUserAccountsCallCount);
    }

    [Fact]
    public void CacheInvalidationVersion_IncrementsAtomically()
    {
        var version = new CacheInvalidationVersion();

        var initial = version.Current;
        var next = version.Invalidate();

        Assert.Equal(initial + 1, next);
        Assert.Equal(next, version.Current);
    }

    [Fact]
    public void CacheInvalidationVersion_TracksDomainGenerationsIndependently()
    {
        var version = new CacheInvalidationVersion();

        var initialAccounting = version.Accounting;
        var initialDonations = version.Donations;
        var initialAggregate = version.Current;

        version.Invalidate(CacheDataDomains.Accounting);

        Assert.Equal(initialAccounting + 1, version.Accounting);
        Assert.Equal(initialDonations, version.Donations);
        Assert.Equal(initialAggregate + 1, version.Current);
        Assert.Equal(version.Accounting, version.GetGeneration(CacheDataDomains.Accounting));
    }

    [Fact]
    public void CacheInvalidationVersion_CompositeGenerationChangesWhenAnySnapshotDomainChanges()
    {
        var version = new CacheInvalidationVersion();
        var domains = CacheDataDomains.Donations | CacheDataDomains.Accounting | CacheDataDomains.AccountMetadata;
        var initial = version.GetGeneration(domains);

        version.Invalidate(CacheDataDomains.Donations);

        Assert.NotEqual(initial, version.GetGeneration(domains));
        Assert.NotEqual(version.Current, version.GetGeneration(domains));
    }

    private sealed class FakeUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(id == user.Id ? user : null);
        public Task<List<User>> GetAllAsync() => Task.FromResult(new List<User> { user });
        public Task<User> AddAsync(User entity) => Task.FromResult(entity);
        public Task<User> UpdateAsync(User entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(id == user.Id);
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(email == user.Email ? user : null);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult<User?>(null);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(new List<User> { user });
        public Task<bool> ExistsAsync(string email) => Task.FromResult(email == user.Email);
    }

    private sealed class FakeUserAccountAccessRepository(Account account) : IUserAccountAccessRepository
    {
        public int GetUserAccountsCallCount { get; private set; }

        public Task<List<Account>> GetUserAccountsAsync(int userId)
        {
            GetUserAccountsCallCount++;
            return Task.FromResult(new List<Account> { account });
        }
        public Task<List<User>> GetAccountUsersAsync(int accountId) => Task.FromResult(new List<User>());
        public Task<Dictionary<int, List<int>>> GetAccountUserIdsAsync() => Task.FromResult(new Dictionary<int, List<int>>());
        public Task<Account?> GetAccountByIdAsync(int accountId) => Task.FromResult<Account?>(account.AccountId == accountId ? account : null);
        public Task<bool> HasAccessAsync(int userId, int accountId) => Task.FromResult(account.AccountId == accountId);
        public Task<bool> GrantAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAllAccessAsync(int userId) => Task.FromResult(true);
        public Task<int> GetUserAccountCountAsync(int userId) => Task.FromResult(1);
        public Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId) => Task.FromResult(true);
    }

    private sealed class FakeAccountRepository(Account account) : IAccountRepository
    {
        public int GetAllCallCount { get; private set; }
        public Task<Account?> GetByIdAsync(int id) => Task.FromResult<Account?>(id == account.AccountId ? account : null);
        public Task<List<Account>> GetAllAsync()
        {
            GetAllCallCount++;
            return Task.FromResult(new List<Account> { account });
        }
        public Task<Account> AddAsync(Account entity) => Task.FromResult(entity);
        public Task<Account> UpdateAsync(Account entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(id == account.AccountId);
        public Task<Account?> GetByFundCodeAsync(string fundCode) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByFundAsync(string fund) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByAccountNumberAsync(string accountNumber) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetByUserIdAsync(string userId) => Task.FromResult(new List<Account> { account });
        public Task<bool> ValidateUserAccessAsync(string userId, string fund) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string fundCode) => Task.FromResult(false);
    }

    private sealed class FakeExpenseReadRepository : IExpenseReadRepository
    {
        public Task<IReadOnlyDictionary<int, List<AccountingRecord>>> GetAccountingDataForAccountsAsync(IReadOnlyList<(int AccountId, string AccountingClass, string AccountNumber)> accounts, DateTime startDate, DateTime endDate) => Task.FromResult<IReadOnlyDictionary<int, List<AccountingRecord>>>(new Dictionary<int, List<AccountingRecord>>());
        public int CallCount { get; private set; }
        public Task<List<AccountingRecord>> GetAccountingDataForAccountAsync(string accountingClass, string accountNumber, DateTime startDate, DateTime endDate) => Load();
        private Task<List<AccountingRecord>> Load()
        {
            CallCount++;
            return Task.FromResult(new List<AccountingRecord>());
        }
    }

    private sealed class FakeDonationReadRepository : IDonationReadRepository
    {
        public Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int accountId, string fundName, DateTime startDate, DateTime endDate) => Load();
        public int CallCount { get; private set; }
        public Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId) => Task.FromResult(new List<SubAccount>());
        public Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames) => Load();
        public Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName) => Load();
        public Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate) => Load();
        public Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate) => Load();
        public Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate) => Load();
        public Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName) => Load();
        public Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName) => Load();
        public Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm) => Load();
        public Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm) => Load();
        public Task<List<DonationRecord>> GetInternDonationsByInternNameAndDateRangeAsync(string internName, DateTime startDate, DateTime endDate) => Load();
        private Task<List<DonationRecord>> Load()
        {
            CallCount++;
            return Task.FromResult(new List<DonationRecord>());
        }
    }
}
