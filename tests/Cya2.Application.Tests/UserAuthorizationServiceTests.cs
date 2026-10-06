using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class UserAuthorizationServiceTests
{
    [Fact]
    public async Task ValidateUserAsync_RejectsDeletedUser()
    {
        var service = CreateService(null, new Dictionary<int, HashSet<int>>());

        var result = await service.ValidateUserAsync("deleted@example.test");

        Assert.False(result.IsAuthorized);
        Assert.False(result.UserExists);
    }

    [Fact]
    public async Task ValidateUserAsync_UsesCurrentDatabaseRole()
    {
        var user = new User { Id = 7, Email = "admin@example.test", AuthLevel = "User" };
        var service = CreateService(user, new Dictionary<int, HashSet<int>>());

        var result = await service.ValidateUserAsync("7");

        Assert.True(result.IsAuthorized);
        Assert.False(result.IsAdmin);
        Assert.False(result.CanAccessAllAccounts);
    }

    [Fact]
    public async Task ValidateAccountAccessAsync_RejectsRevokedAccount()
    {
        var user = new User { Id = 7, Email = "user@example.test", AuthLevel = "User" };
        var service = CreateService(user, new Dictionary<int, HashSet<int>> { [7] = [12] });

        var result = await service.ValidateAccountAccessAsync("7", 13);

        Assert.False(result.IsAuthorized);
        Assert.True(result.UserExists);
    }

    [Fact]
    public async Task ValidateAccountAccessAsync_AllowsCurrentAccountMembership()
    {
        var user = new User { Id = 7, Email = "user@example.test", AuthLevel = "User" };
        var service = CreateService(user, new Dictionary<int, HashSet<int>> { [7] = [12] });

        var result = await service.ValidateAccountAccessAsync("7", 12);

        Assert.True(result.IsAuthorized);
    }

    private static UserAuthorizationService CreateService(User? user, Dictionary<int, HashSet<int>> memberships)
    {
        return new UserAuthorizationService(
            new FakeUserRepository(user),
            new FakeAccountAccessRepository(memberships),
            NullLogger<UserAuthorizationService>.Instance);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly User? _user;
        public FakeUserRepository(User? user) => _user = user;
        public Task<User?> GetByIdAsync(int id) => Task.FromResult(_user?.Id == id ? _user : null);
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_user?.Email.Equals(email, StringComparison.OrdinalIgnoreCase) == true ? _user : null);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult(_user?.GoogleId == externalId ? _user : null);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(_user is null ? new List<User>() : new List<User> { _user });
        public Task<List<User>> GetAllAsync() => GetActiveUsersAsync();
        public Task<User> AddAsync(User entity) => Task.FromResult(entity);
        public Task<User> UpdateAsync(User entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(_user?.Id == id);
        public Task<bool> ExistsAsync(string email) => Task.FromResult(_user?.Email.Equals(email, StringComparison.OrdinalIgnoreCase) == true);
    }

    private sealed class FakeAccountAccessRepository : IUserAccountAccessRepository
    {
        private readonly Dictionary<int, HashSet<int>> _memberships;
        public FakeAccountAccessRepository(Dictionary<int, HashSet<int>> memberships) => _memberships = memberships;
        public Task<bool> HasAccessAsync(int userId, int accountId) => Task.FromResult(_memberships.TryGetValue(userId, out var accounts) && accounts.Contains(accountId));
        public Task<List<Account>> GetUserAccountsAsync(int userId) => Task.FromResult(new List<Account>());
        public Task<Account?> GetAccountByIdAsync(int accountId) => Task.FromResult<Account?>(null);
        public Task<bool> GrantAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAccessAsync(int userId, int accountId) => Task.FromResult(true);
        public Task<bool> RevokeAllAccessAsync(int userId) => Task.FromResult(true);
        public Task<int> GetUserAccountCountAsync(int userId) => Task.FromResult(0);
        public Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId) => Task.FromResult(true);
    }
}
