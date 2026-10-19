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

public sealed class ExpenseServiceTests
{
    [Fact]
    public async Task GetExpenseDataAsync_EmptyInitialSelection_ResolvesAuthorizedDefaultAccount()
    {
        var user = new User
        {
            Id = 7,
            Email = "user@example.test",
            AuthLevel = "User",
            DefaultAccount = 12
        };
        var account = new Account
        {
            AccountId = 12,
            Fund = "General Fund",
            AccountingClass = "General",
            AccountNumber = "1000"
        };
        var userRepository = new FakeUserRepository(user);
        var accessRepository = new FakeAccountAccessRepository(account);
        var contextService = new UserAccountContextService(
            userRepository,
            accessRepository,
            new FakeAccountRepository(),
            new FakeCacheInvalidationVersion(),
            NullLogger<UserAccountContextService>.Instance);
        var authorizationService = new UserAuthorizationService(
            userRepository,
            accessRepository,
            NullLogger<UserAuthorizationService>.Instance);
        var service = new ExpenseService(
            NullLogger<ExpenseService>.Instance,
            contextService,
            new FakeAccountFactDataService(),
            new AccountingTransactionProcessor(new ExpenseClassificationService()),
            authorizationService);

        var result = await service.GetExpenseDataAsync(
            string.Empty,
            new DateRange(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow),
            "7");

        var availableAccount = Assert.Single(result.UserAccounts);
        Assert.Equal(account.Fund, availableAccount.Fund);
        Assert.Equal(account.Fund, result.SelectedAccount);
    }

    private sealed class FakeUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(id == user.Id ? user : null);
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase) ? user : null);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult<User?>(null);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(new List<User> { user });
        public Task<List<User>> GetAllAsync() => GetActiveUsersAsync();
        public Task<User> AddAsync(User entity) => Task.FromResult(entity);
        public Task<User> UpdateAsync(User entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(id == user.Id);
        public Task<bool> ExistsAsync(string email) => Task.FromResult(string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeAccountAccessRepository(Account account) : IUserAccountAccessRepository
    {
        public Task<List<Account>> GetUserAccountsAsync(int userId) => Task.FromResult(new List<Account> { account });
        public Task<List<User>> GetAccountUsersAsync(int accountId) => Task.FromResult(new List<User>());
        public Task<Dictionary<int, List<int>>> GetAccountUserIdsAsync() => Task.FromResult(new Dictionary<int, List<int>>());
        public Task<Account?> GetAccountByIdAsync(int accountId) => Task.FromResult<Account?>(accountId == account.AccountId ? account : null);
        public Task<bool> HasAccessAsync(int userId, int accountId) => Task.FromResult(accountId == account.AccountId);
        public Task<bool> GrantAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAllAccessAsync(int userId) => Task.FromResult(true);
        public Task<int> GetUserAccountCountAsync(int userId) => Task.FromResult(1);
        public Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId) => Task.FromResult(true);
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        public Task<Account?> GetByFundCodeAsync(string fundCode) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByFundAsync(string fund) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByAccountNumberAsync(string accountNumber) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetByUserIdAsync(string userId) => Task.FromResult(new List<Account>());
        public Task<bool> ValidateUserAccessAsync(string userId, string fund) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string fundCode) => Task.FromResult(false);
        public Task<Account?> GetByIdAsync(int id) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetAllAsync() => Task.FromResult(new List<Account>());
        public Task<Account> AddAsync(Account entity) => Task.FromResult(entity);
        public Task<Account> UpdateAsync(Account entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(false);
    }

    private sealed class FakeCacheInvalidationVersion : ICacheInvalidationVersion
    {
        public long Current => 1;
        public long Donations => 1;
        public long Accounting => 1;
        public long AccountMetadata => 1;
        public long DonorContacts => 1;
        public long Authorization => 1;
        public long GetGeneration(CacheDataDomains domains) => 1;
        public long Invalidate() => 1;
        public long Invalidate(CacheDataDomains domains) => 1;
    }

    private sealed class FakeAccountFactDataService : IAccountFactDataService
    {
        public Task<DonationCalculationData> GetDonationDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => Task.FromResult(new DonationCalculationData());
        public Task<List<AccountingRecord>> GetAccountingDataAsync(UserAccountContextAccount account, string userId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default) => Task.FromResult(new List<AccountingRecord>());
    }
}
