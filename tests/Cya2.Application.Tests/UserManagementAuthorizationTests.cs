using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class UserManagementAuthorizationTests
{
    [Fact]
    public async Task StaffCache_BackgroundMetadataDoesNotLoadStaff_AndAdminCanLoadAfterwards()
    {
        var repositories = new RecordingRepositories();
        repositories.Users.AllUsers.Add(new User { Id = 7, Name = "Test", Email = "test@example.invalid" });
        var authorization = new FakeAdminAuthorizationContext(false);
        var cache = new AdminSharedCache();
        var versions = new CacheInvalidationVersion();
        cache.SetAccounts(versions.Current, []);
        cache.SetSubAccounts(versions.Current, []);
        var preload = CreateStaffPreload(repositories, authorization, cache, versions);

        await preload.PreloadMetadataAsync();

        Assert.Equal(0, repositories.Users.AllCalls);
        Assert.False(cache.TryGetStaff(versions.Current, out _));
        authorization.IsAdmin = true;
        Assert.Single(await preload.GetStaffAsync());
        Assert.Equal(1, repositories.Users.AllCalls);
    }

    [Fact]
    public async Task StaffCache_FailureIsRetryable_AndCachedStaffRequiresAuthorization()
    {
        var repositories = new RecordingRepositories();
        var authorization = new FakeAdminAuthorizationContext(true);
        var cache = new AdminSharedCache();
        var versions = new CacheInvalidationVersion();
        var preload = CreateStaffPreload(repositories, authorization, cache, versions);
        repositories.Users.Failure = new InvalidOperationException("sensitive value");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => preload.GetStaffAsync());
        Assert.DoesNotContain("sensitive value", failure.ToString());
        Assert.False(cache.TryGetStaff(versions.Current, out _));

        repositories.Users.Failure = null;
        Assert.Empty(await preload.GetStaffAsync());
        Assert.True(cache.TryGetStaff(versions.Current, out _));
        Assert.Equal(2, repositories.Users.AllCalls);
        authorization.IsAdmin = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preload.GetStaffAsync());
        Assert.Equal(2, repositories.Users.AllCalls);
        authorization.IsAdmin = true;
        Assert.Empty(await preload.GetStaffAsync());
        Assert.Equal(2, repositories.Users.AllCalls);
    }

    private static AdminPreloadService CreateStaffPreload(RecordingRepositories repositories, FakeAdminAuthorizationContext authorization,
        AdminSharedCache cache, CacheInvalidationVersion versions)
        => new(null!, CreateService(repositories, authorization), null!, cache, null!, null!, new EmptyReferences(),
            versions, null!, null!, null!, NullLogger<AdminPreloadService>.Instance);

    private sealed class EmptyReferences : IFundReferenceService
    {
        public Task<IReadOnlyList<FundReferenceDto>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FundReferenceDto>>([]);
        public IReadOnlyList<FundReferenceDto> FilterForUser(IReadOnlyList<FundReferenceDto> references, int userId, bool canAccessAllAccounts) => references;
    }

    [Theory]
    [InlineData("jane.doe", "jane.doe@scene8.net")]
    [InlineData("  jane+test  ", "jane+test@scene8.net")]
    public void OrganizationUsername_ConstructsAddress(string username, string expected)
    {
        Assert.True(UserManagementService.TryBuildOrganizationEmail(username, out var email));
        Assert.Equal(expected, email);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("jane@scene8.net")]
    [InlineData("jane@example.test")]
    [InlineData("jane doe")]
    [InlineData("jane..doe")]
    public void OrganizationUsername_RejectsInvalidInput(string username)
    {
        Assert.False(UserManagementService.TryBuildOrganizationEmail(username, out _));
    }

    [Theory]
    [InlineData("outside@example.test")]
    [InlineData("user@scene8.net.example.test")]
    [InlineData("user@@scene8.net")]
    [InlineData("Display <user@scene8.net>")]
    [InlineData("user name@scene8.net")]
    public async Task CreatingUser_RejectsInvalidOrganizationAddress(string email)
    {
        var repositories = new RecordingRepositories();
        var result = await CreateService(repositories, isAdmin: true).CreateAdminUserAsync("Test User", email, "User", []);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, repositories.UserAdds);
    }

    [Fact]
    public async Task CreatingUser_PersistsFullOrganizationAddress()
    {
        var repositories = new RecordingRepositories();
        var result = await CreateService(repositories, isAdmin: true).CreateAdminUserAsync("Test User", " test@SCENE8.NET ", "User", []);
        Assert.True(result.IsSuccess);
        Assert.Equal("test@SCENE8.NET", repositories.Users.AddedUser!.Email);
    }

    [Fact]
    public async Task UpdatingExternalUser_RequiresReplacementBeforeMutation()
    {
        var repositories = new RecordingRepositories();
        var user = new User { Id = 2, Name = "Original", Email = "old@example.test", AuthLevel = "User" };
        repositories.Users.UserById = user;
        var service = CreateService(repositories, isAdmin: true);

        var rejected = await service.UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = 2, Name = "Changed" });
        Assert.False(rejected.IsSuccess);
        Assert.Equal("Original", user.Name);
        Assert.Equal("old@example.test", user.Email);
        Assert.Equal(0, repositories.UserUpdates);

        var accepted = await service.UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = 2, Name = "Changed", Email = "new@scene8.net" });
        Assert.True(accepted.IsSuccess);
        Assert.Equal("new@scene8.net", user.Email);
        Assert.Equal("Changed", user.Name);
    }

    [Fact]
    public async Task UpdatingUser_RejectsDuplicateEmailWithoutMutation()
    {
        var repositories = new RecordingRepositories();
        var user = new User { Id = 2, Name = "Original", Email = "original@scene8.net", AuthLevel = "User" };
        repositories.Users.UserById = user;
        repositories.Users.UserByEmail = new User { Id = 3, Email = "other@scene8.net" };
        var result = await CreateService(repositories, isAdmin: true).UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = 2, Name = "Changed", Email = "other@scene8.net" });
        Assert.False(result.IsSuccess);
        Assert.Equal("Original", user.Name);
        Assert.Equal("original@scene8.net", user.Email);
        Assert.Equal(0, repositories.UserUpdates);
    }

    [Fact]
    public async Task CreatingUser_RepositoryFailureDoesNotExposeSensitiveException()
    {
        const string sentinel = "private-person@example.invalid / private-fund";
        var repositories = new RecordingRepositories();
        repositories.Users.Failure = new InvalidOperationException(sentinel);
        var logger = new RecordingLogger<UserManagementService>();
        var service = new UserManagementService(repositories.Users, repositories.AccountAccess, repositories.Accounts,
            new FakeAdminAuthorizationContext(isAdmin: true), logger);

        var result = await service.CreateAdminUserAsync("Test User", "test@scene8.net", "User", []);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(sentinel, result.Message);
        Assert.DoesNotContain(sentinel, string.Join(" ", logger.Entries));
        Assert.Contains("InvalidOperationException", string.Join(" ", logger.Entries));
    }

    [Fact]
    public async Task AuthorizationFailure_DoesNotLogFallbackEmailOrException()
    {
        const string sentinel = "private-person@example.invalid";
        var repositories = new RecordingRepositories();
        repositories.Users.Failure = new InvalidOperationException(sentinel);
        var logger = new RecordingLogger<UserAuthorizationService>();
        var service = new UserAuthorizationService(repositories.Users, repositories.AccountAccess, logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateUserAsync(sentinel));

        Assert.DoesNotContain(sentinel, string.Join(" ", logger.Entries));
        Assert.Contains("InvalidOperationException", string.Join(" ", logger.Entries));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception) + exception?.ToString());
    }

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
    public async Task CreatingIntern_InvalidatesAuthorizationAndAccountMetadataCaches()
    {
        var repositories = new RecordingRepositories();
        var cacheInvalidator = new RecordingCacheInvalidator();
        var service = CreateService(repositories, new FakeAdminAuthorizationContext(isAdmin: true), cacheInvalidator);

        var result = await service.CreateAdminUserAsync("Test Intern", "intern@scene8.net", "Intern", []);

        Assert.True(result.IsSuccess);
        Assert.Contains(CacheDataDomains.Authorization | CacheDataDomains.AccountMetadata, cacheInvalidator.InvalidatedDomains);
    }

    [Fact]
    public async Task CreatingIntern_CreatesAccountWithoutAccountingClass()
    {
        var repositories = new RecordingRepositories();
        var service = CreateService(repositories, isAdmin: true);

        var result = await service.CreateAdminUserAsync("Test Intern", "intern@scene8.net", "Intern", []);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repositories.Accounts.AddedAccount);
        Assert.Equal("Intern: Test Intern", repositories.Accounts.AddedAccount!.Fund);
        Assert.Null(repositories.Accounts.AddedAccount.AccountingClass);
    }

    [Fact]
    public async Task CreatingIntern_WithExistingDesignationRequiresReuseConfirmation()
    {
        var repositories = new RecordingRepositories();
        repositories.Accounts.ExistingByFund = new Account { AccountId = 12, Fund = "Intern: Test Intern", AccountNumber = "INT-12" };
        var service = CreateService(repositories, isAdmin: true);

        var result = await service.CreateAdminUserAsync("Test Intern", "intern@scene8.net", "Intern", []);

        Assert.False(result.IsSuccess);
        Assert.True(result.RequiresInternAccountReuse);
        Assert.Equal(0, repositories.UserAdds);
    }

    [Fact]
    public async Task CreatingIntern_WithConfirmedExistingDesignationReusesAccount()
    {
        var repositories = new RecordingRepositories();
        repositories.Accounts.ExistingByFund = new Account { AccountId = 12, Fund = "Intern: Test Intern", AccountNumber = "INT-12" };
        var service = CreateService(repositories, isAdmin: true);

        var result = await service.CreateAdminUserAsync("Test Intern", "intern@scene8.net", "Intern", [], reuseExistingInternAccount: true);

        Assert.True(result.IsSuccess);
        Assert.Null(repositories.Accounts.AddedAccount);
        Assert.Equal(1, repositories.AccountAccess.AccessGrants);
    }

    [Fact]
    public async Task RenamingIntern_RenamesLinkedInternAccount()
    {
        var repositories = new RecordingRepositories();
        repositories.Users.UserById = new User { Id = 3, Name = "Original Intern", Email = "intern@scene8.net", AuthLevel = "Intern" };
        repositories.AccountAccess.UserAccounts.Add(new Account { AccountId = 12, Fund = "Intern: Original Intern", AccountNumber = "INT-3" });
        var invalidator = new RecordingCacheInvalidator();
        var service = CreateService(repositories, new FakeAdminAuthorizationContext(isAdmin: true), invalidator);

        var result = await service.UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = 3, Name = "Renamed Intern" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Intern: Renamed Intern", repositories.Accounts.UpdatedAccount!.Fund);
        Assert.Contains(CacheDataDomains.Authorization | CacheDataDomains.AccountMetadata, invalidator.InvalidatedDomains);
    }

    [Theory]
    [InlineData("User")]
    [InlineData("Viewer")]
    [InlineData("Admin")]
    public async Task ChangingInternRole_IsRejectedWithoutPartialChanges(string role)
    {
        var repositories = new RecordingRepositories();
        var user = new User { Id = 3, Name = "Test Intern", Email = "intern@scene8.net", AuthLevel = "Intern" };
        repositories.Users.UserById = user;
        repositories.AccountAccess.UserAccounts.Add(new Account { AccountId = 12, Fund = "Intern: Test Intern", AccountNumber = "INT-3" });
        var cacheInvalidator = new RecordingCacheInvalidator();
        var service = CreateService(repositories, new FakeAdminAuthorizationContext(isAdmin: true), cacheInvalidator);

        var result = await service.UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = 3, Name = "Changed", Email = "changed@scene8.net", AuthLevel = role });

        Assert.False(result.IsSuccess);
        Assert.Contains("new user", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Test Intern", user.Name);
        Assert.Equal("intern@scene8.net", user.Email);
        Assert.Equal("Intern", user.AuthLevel);
        Assert.Equal(0, repositories.UserUpdates);
        Assert.Equal(0, repositories.AccessRevokes);
        Assert.Equal(0, repositories.AccessGrants);
        Assert.Empty(cacheInvalidator.InvalidatedDomains);
    }

    [Fact]
    public async Task UpdatingInternEmail_WithUnchangedRole_Succeeds()
    {
        var repositories = new RecordingRepositories();
        repositories.Users.UserById = new User { Id = 3, Name = "Test Intern", Email = "intern@scene8.net", AuthLevel = "Intern" };
        repositories.AccountAccess.UserAccounts.Add(new Account { AccountId = 12, Fund = "Intern: Test Intern", AccountNumber = "INT-3" });
        var service = CreateService(repositories, isAdmin: true);

        var result = await service.UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = 3, Email = "changed@scene8.net", AuthLevel = "intern" });

        Assert.True(result.IsSuccess);
        Assert.Equal("changed@scene8.net", repositories.Users.UserById.Email);
        Assert.Equal("Intern", repositories.Users.UserById.AuthLevel);
        Assert.Equal(1, repositories.UserUpdates);
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
        IAdminAuthorizationContext authorizationContext,
        IImportCacheInvalidator? cacheInvalidator = null)
        => new(
            repositories.Users,
            repositories.AccountAccess,
            repositories.Accounts,
            authorizationContext,
            NullLogger<UserManagementService>.Instance,
            cacheInvalidator);

    private sealed class FakeAdminAuthorizationContext(bool isAdmin) : IAdminAuthorizationContext
    {
        public bool IsAdmin { get; set; } = isAdmin;
        public Task<bool> IsCurrentUserAdminAsync() => Task.FromResult(IsAdmin);
        public Task<int?> GetCurrentAdminUserIdAsync() => Task.FromResult(IsAdmin ? 1 : (int?)null);
    }

    private sealed class RecordingCacheInvalidator : IImportCacheInvalidator
    {
        public List<CacheDataDomains> InvalidatedDomains { get; } = new();

        public void InvalidateAll() => InvalidatedDomains.Add(CacheDataDomains.All);

        public void Invalidate(CacheDataDomains domains) => InvalidatedDomains.Add(domains);
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
        public Exception? Failure { get; set; }
        public List<User> AllUsers { get; } = [];
        public int AllCalls { get; private set; }
        public int UserUpdates { get; private set; }
        public int UserAdds { get; private set; }
        public int UserDeletes { get; private set; }

        public User? UserById { get; set; }

        public User? UserByEmail { get; set; }
        public User? AddedUser { get; private set; }
        public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(UserById ?? new User { Id = id, Name = "Existing", Email = "existing@scene8.net", AuthLevel = "User" });
        public Task<User?> GetByEmailAsync(string email) => Failure is null ? Task.FromResult(UserByEmail) : Task.FromException<User?>(Failure);
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult<User?>(null);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(new List<User>());
        public Task<List<User>> GetAllAsync()
        {
            AllCalls++;
            return Failure is null ? Task.FromResult(AllUsers.ToList()) : Task.FromException<List<User>>(Failure);
        }
        public Task<User> AddAsync(User entity) { UserAdds++; entity.Id = 3; AddedUser = entity; return Task.FromResult(entity); }
        public Task<User> UpdateAsync(User entity) { UserUpdates++; return Task.FromResult(entity); }
        public Task DeleteAsync(int id) { UserDeletes++; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(int id) => Task.FromResult(true);
        public Task<bool> ExistsAsync(string email) => Failure is null ? Task.FromResult(false) : Task.FromException<bool>(Failure);

    }

    private sealed class RecordingAccountAccessRepository : IUserAccountAccessRepository
    {
        public int AccessGrants { get; private set; }
        public int AccessRevokes { get; private set; }
        public List<Account> UserAccounts { get; } = new();

        public Task<List<Account>> GetUserAccountsAsync(int userId) => Task.FromResult(UserAccounts.ToList());
        public Task<List<User>> GetAccountUsersAsync(int accountId) => Task.FromResult(new List<User>());
        public Task<Dictionary<int, List<int>>> GetAccountUserIdsAsync() => Task.FromResult(new Dictionary<int, List<int>>());
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
        public Account? AddedAccount { get; private set; }
        public Account? ExistingByFund { get; set; }
        public Account? UpdatedAccount { get; private set; }

        public Task<Account?> GetByIdAsync(int id) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetAllAsync() => Task.FromResult(new List<Account>());
        public Task<Account> AddAsync(Account entity)
        {
            AddedAccount = entity;
            entity.AccountId = 4;
            return Task.FromResult(entity);
        }
        public Task<Account> UpdateAsync(Account entity)
        {
            UpdatedAccount = entity;
            return Task.FromResult(entity);
        }
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(false);
        public Task<Account?> GetByFundCodeAsync(string fundCode) => Task.FromResult<Account?>(null);
        public Task<Account?> GetByFundAsync(string fund) => Task.FromResult(
            string.Equals(ExistingByFund?.Fund, fund, StringComparison.OrdinalIgnoreCase) ? ExistingByFund : null);
        public Task<Account?> GetByAccountNumberAsync(string accountNumber) => Task.FromResult<Account?>(null);
        public Task<List<Account>> GetByUserIdAsync(string userId) => Task.FromResult(new List<Account>());
        public Task<bool> ValidateUserAccessAsync(string userId, string fund) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string fundCode) => Task.FromResult(false);
    }
}
