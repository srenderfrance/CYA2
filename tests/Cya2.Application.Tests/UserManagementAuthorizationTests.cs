using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class UserManagementAuthorizationTests
{
    [Fact]
    public async Task RegularUser_CannotUpdateUser()
    {
        var repositories = new RecordingRepositories();
        var service = CreateService(repositories, isAdmin: false);

        var result = await service.UpdateAdminUserAsync(new AdminUserUpdateDto
        {
            UserId = 2,
            Name = "Changed"
        });

        Assert.False(result.IsSuccess);
        Assert.Contains("authorization", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, repositories.UserUpdates);
    }

    [Fact]
    public async Task RegularUser_CannotCreateOrDeleteUser()
    {
        var repositories = new RecordingRepositories();
        var service = CreateService(repositories, isAdmin: false);

        var create = await service.CreateAdminUserAsync("New User", "new@example.test", "User", []);
        var delete = await service.DeleteAdminUserAsync(2);

        Assert.False(create.IsSuccess);
        Assert.False(delete.IsSuccess);
        Assert.Equal(0, repositories.UserAdds);
        Assert.Equal(0, repositories.UserDeletes);
    }

    [Fact]
    public async Task RegularUser_CannotGrantOrRevokeAccountAccess()
    {
        var repositories = new RecordingRepositories();
        var service = CreateService(repositories, isAdmin: false);

        var grant = await service.GrantAccountAccessAsync(2, 3);
        var revoke = await service.RevokeAccountAccessAsync(2, 3);

        Assert.False(grant.IsSuccess);
        Assert.False(revoke.IsSuccess);
        Assert.Equal(0, repositories.AccessGrants);
        Assert.Equal(0, repositories.AccessRevokes);
    }

    [Fact]
    public async Task CurrentAdmin_CanUpdateUser()
    {
        var repositories = new RecordingRepositories();
        var service = CreateService(repositories, isAdmin: true);

        var result = await service.UpdateAdminUserAsync(new AdminUserUpdateDto
        {
            UserId = 2,
            Name = "Changed"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repositories.UserUpdates);
    }

    [Fact]
    public async Task RevokedAdmin_CannotPerformAdministrativeChanges()
    {
        var repositories = new RecordingRepositories();
        var authorization = new FakeAdminAuthorizationContext(isAdmin: true);
        var service = CreateService(repositories, authorization);

        authorization.IsAdmin = false;

        var update = await service.UpdateAdminUserAsync(new AdminUserUpdateDto
        {
            UserId = 2,
            Name = "Changed"
        });
        var create = await service.CreateAdminUserAsync("New User", "new@example.test", "User", []);
        var delete = await service.DeleteAdminUserAsync(2);
        var grant = await service.GrantAccountAccessAsync(2, 3);
        var revoke = await service.RevokeAccountAccessAsync(2, 3);

        Assert.False(update.IsSuccess);
        Assert.False(create.IsSuccess);
        Assert.False(delete.IsSuccess);
        Assert.False(grant.IsSuccess);
        Assert.False(revoke.IsSuccess);
        Assert.Equal(0, repositories.UserUpdates);
        Assert.Equal(0, repositories.UserAdds);
        Assert.Equal(0, repositories.UserDeletes);
        Assert.Equal(0, repositories.AccessGrants);
        Assert.Equal(0, repositories.AccessRevokes);
    }

    private static UserManagementService CreateService(RecordingRepositories repositories, bool isAdmin)
        => CreateService(repositories, new FakeAdminAuthorizationContext(isAdmin));

    private static UserManagementService CreateService(
        RecordingRepositories repositories,
        IAdminAuthorizationContext authorizationContext)
        => new(
            repositories.Users,
            repositories.AccountAccess,
            repositories.Accounts,
            authorizationContext,
            NullLogger<UserManagementService>.Instance);

    private sealed class FakeAdminAuthorizationContext(bool isAdmin) : IAdminAuthorizationContext
    {
        public bool IsAdmin { get; set; } = isAdmin;
        public Task<bool> IsCurrentUserAdminAsync() => Task.FromResult(IsAdmin);
    }

    private sealed class RecordingRepositories
    {
        public RecordingUserRepository Users { get; } = new();
        public RecordingAccountAccessRepository AccountAccess { get; } = new();
        public RecordingAccountRepository Accounts { get; } = new();

        public int UserUpdates => Users.UserUpdates;
        public int UserAdds => Users.UserAdds;
        public int UserDeletes => Users.UserDeletes;
        public int AccessGrants => AccountAccess.AccessGrants;
        public int AccessRevokes => AccountAccess.AccessRevokes;
    }

    private sealed class RecordingUserRepository : IUserRepository
    {
        public int UserUpdates { get; private set; }
        public int UserAdds { get; private set; }
        public int UserDeletes { get; private set; }

        public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(new User { Id = id, Name = "Existing", Email = "existing@example.test", AuthLevel = "User" });
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(null);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult<User?>(null);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(new List<User>());
        public Task<List<User>> GetAllAsync() => Task.FromResult(new List<User>());
        public Task<User> AddAsync(User entity) { UserAdds++; entity.Id = 3; return Task.FromResult(entity); }
        public Task<User> UpdateAsync(User entity) { UserUpdates++; return Task.FromResult(entity); }
        public Task DeleteAsync(int id) { UserDeletes++; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(int id) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string email) => Task.FromResult(false);

    }

    private sealed class RecordingAccountAccessRepository : IUserAccountAccessRepository
    {
        public int AccessGrants { get; private set; }
        public int AccessRevokes { get; private set; }

        public Task<List<Account>> GetUserAccountsAsync(int userId) => Task.FromResult(new List<Account>());
        public Task<Account?> GetAccountByIdAsync(int accountId) => Task.FromResult<Account?>(new Account { AccountId = accountId, Fund = "Test" });
        public Task<bool> HasAccessAsync(int userId, int accountId) => Task.FromResult(false);
        public Task<bool> GrantAccessAsync(int userId, int accountId) { AccessGrants++; return Task.FromResult(true); }
        public Task<bool> RevokeAccessAsync(int userId, int accountId) { AccessRevokes++; return Task.FromResult(true); }
        public Task<bool> RevokeAllAccessAsync(int userId) => Task.FromResult(true);
        public Task<int> GetUserAccountCountAsync(int userId) => Task.FromResult(0);
        public Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId) => Task.FromResult(true);

    }

    private sealed class RecordingAccountRepository : IAccountRepository
    {
        public Task<Account?> GetByIdAsync(int id) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetAllAsync() => Task.FromResult(new List<Account>());
        public Task<Account> AddAsync(Account entity) => Task.FromResult(entity);
        public Task<Account> UpdateAsync(Account entity) => Task.FromResult(entity);
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(false);
        public Task<Account?> GetByFundCodeAsync(string fundCode) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByFundAsync(string fund) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByAccountNumberAsync(string accountNumber) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetByUserIdAsync(string userId) => Task.FromResult(new List<Account>());
        public Task<bool> ValidateUserAccessAsync(string userId, string fund) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string fundCode) => Task.FromResult(false);
    }
}
