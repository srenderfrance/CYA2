using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class GoogleSignInServiceTests
{
    [Fact]
    public async Task ResolveUserAsync_MatchesExternalId_ReturnsUser()
    {
        var user = CreateUser("google-1", "user@example.test");
        var repository = new FakeUserRepository(user);

        var result = await CreateService(repository).ResolveUserAsync("google-1", "user@example.test");

        Assert.True(result.IsAuthorized);
        Assert.Same(user, result.User);
    }

    [Fact]
    public async Task ResolveUserAsync_EmailMatchWithoutGoogleId_BindsGoogleId()
    {
        var user = CreateUser(string.Empty, "user@example.test");
        var repository = new FakeUserRepository(user);

        var result = await CreateService(repository).ResolveUserAsync("google-2", "user@example.test");

        Assert.True(result.IsAuthorized);
        Assert.Equal("google-2", user.GoogleId);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public async Task ResolveUserAsync_RejectsUnregisteredUser()
    {
        var result = await CreateService(new FakeUserRepository()).ResolveUserAsync("google-3", "unknown@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal("User not registered", result.RejectionReason);
    }

    [Fact]
    public async Task ResolveUserAsync_RejectsGoogleIdMismatch()
    {
        var user = CreateUser("different-google", "user@example.test");

        var result = await CreateService(new FakeUserRepository(user)).ResolveUserAsync("google-4", "user@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal("Google ID mismatch", result.RejectionReason);
    }

    [Fact]
    public async Task ResolveUserAsync_RejectsEmailMismatch()
    {
        var user = CreateUser("google-5", "user@example.test");

        var result = await CreateService(new FakeUserRepository(user)).ResolveUserAsync("google-5", "other@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal("Email mismatch for Google ID", result.RejectionReason);
    }

    [Fact]
    public async Task ResolveUserAsync_EnabledBootstrapAndEmptyDatabase_ProvisionsAdministrator()
    {
        var repository = new FakeUserRepository();

        var result = await CreateService(repository, "admin@example.test", "Initial Administrator")
            .ResolveUserAsync("google-admin", "admin@example.test");

        Assert.True(result.IsAuthorized);
        Assert.Equal("Admin", result.User!.AuthLevel);
        Assert.Equal("Initial Administrator", result.User.Name);
        Assert.Equal("google-admin", result.User.GoogleId);
        Assert.Equal(1, repository.InitialAdminCreateCalls);
    }

    [Fact]
    public async Task ResolveUserAsync_BootstrapEmailMismatch_RejectsUser()
    {
        var repository = new FakeUserRepository();

        var result = await CreateService(repository, "admin@example.test", "Initial Administrator")
            .ResolveUserAsync("google-other", "other@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal(0, repository.InitialAdminCreateCalls);
    }

    [Fact]
    public async Task ResolveUserAsync_BootstrapWithoutName_RejectsUser()
    {
        var repository = new FakeUserRepository();

        var result = await CreateService(repository, "admin@example.test", string.Empty)
            .ResolveUserAsync("google-admin", "admin@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal(0, repository.InitialAdminCreateCalls);
    }

    [Fact]
    public async Task ResolveUserAsync_BootstrapWithExistingUser_RejectsUnknownUser()
    {
        var repository = new FakeUserRepository(CreateUser("existing-google", "existing@example.test"));

        var result = await CreateService(repository, "admin@example.test", "Initial Administrator")
            .ResolveUserAsync("google-admin", "admin@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal(1, repository.InitialAdminCreateCalls);
    }

    private static GoogleSignInService CreateService(
        FakeUserRepository repository,
        string email = "",
        string name = "") =>
        new(repository, Options.Create(new InitialAdminBootstrapOptions
        {
            Email = email,
            Name = name
        }));

    private static User CreateUser(string googleId, string email) => new()
    {
        Id = 1,
        GoogleId = googleId,
        Email = email,
        Name = "Test User",
        AuthLevel = "User"
    };

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users;
        public int UpdateCalls { get; private set; }
        public int InitialAdminCreateCalls { get; private set; }

        public FakeUserRepository(User? user = null) => _users = user is null ? [] : [user];
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult(_users.SingleOrDefault(user => user.GoogleId == externalId));
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_users.SingleOrDefault(user => user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
        public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(null);
        public Task<List<User>> GetAllAsync() => Task.FromResult(_users.ToList());
        public Task<User> AddAsync(User entity) { _users.Add(entity); return Task.FromResult(entity); }
        public Task<User> UpdateAsync(User entity) { UpdateCalls++; return Task.FromResult(entity); }
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(false);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(_users.ToList());
        public Task<bool> ExistsAsync(string email) => Task.FromResult(_users.Any(user => user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
        public Task<User?> TryCreateInitialAdminAsync(User user)
        {
            InitialAdminCreateCalls++;
            if (_users.Count != 0)
            {
                return Task.FromResult<User?>(null);
            }

            _users.Add(user);
            return Task.FromResult<User?>(user);
        }
    }
}
