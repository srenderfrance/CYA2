using Cya2.Application.Services;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class GoogleSignInServiceTests
{
    [Fact]
    public async Task ResolveUserAsync_MatchesExternalId_ReturnsUser()
    {
        var user = CreateUser("google-1", "user@example.test");
        var repository = new FakeUserRepository(user);

        var result = await new GoogleSignInService(repository).ResolveUserAsync("google-1", "user@example.test");

        Assert.True(result.IsAuthorized);
        Assert.Same(user, result.User);
    }

    [Fact]
    public async Task ResolveUserAsync_EmailMatchWithoutGoogleId_BindsGoogleId()
    {
        var user = CreateUser(string.Empty, "user@example.test");
        var repository = new FakeUserRepository(user);

        var result = await new GoogleSignInService(repository).ResolveUserAsync("google-2", "user@example.test");

        Assert.True(result.IsAuthorized);
        Assert.Equal("google-2", user.GoogleId);
        Assert.Equal(1, repository.UpdateCalls);
    }

    [Fact]
    public async Task ResolveUserAsync_RejectsUnregisteredUser()
    {
        var result = await new GoogleSignInService(new FakeUserRepository()).ResolveUserAsync("google-3", "unknown@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal("User not registered", result.RejectionReason);
    }

    [Fact]
    public async Task ResolveUserAsync_RejectsGoogleIdMismatch()
    {
        var user = CreateUser("different-google", "user@example.test");

        var result = await new GoogleSignInService(new FakeUserRepository(user)).ResolveUserAsync("google-4", "user@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal("Google ID mismatch", result.RejectionReason);
    }

    [Fact]
    public async Task ResolveUserAsync_RejectsEmailMismatch()
    {
        var user = CreateUser("google-5", "user@example.test");

        var result = await new GoogleSignInService(new FakeUserRepository(user)).ResolveUserAsync("google-5", "other@example.test");

        Assert.False(result.IsAuthorized);
        Assert.Equal("Email mismatch for Google ID", result.RejectionReason);
    }

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
        private readonly User? _user;
        public int UpdateCalls { get; private set; }

        public FakeUserRepository(User? user = null) => _user = user;
        public Task<User?> GetByExternalIdAsync(string externalId) => Task.FromResult(_user is not null && _user.GoogleId == externalId ? _user : null);
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_user is not null && _user.Email.Equals(email, StringComparison.OrdinalIgnoreCase) ? _user : null);
        public Task<User?> GetByIdAsync(int id) => Task.FromResult<User?>(null);
        public Task<List<User>> GetAllAsync() => Task.FromResult(_user is null ? [] : new List<User> { _user });
        public Task<User> AddAsync(User entity) => Task.FromResult(entity);
        public Task<User> UpdateAsync(User entity) { UpdateCalls++; return Task.FromResult(entity); }
        public Task DeleteAsync(int id) => Task.CompletedTask;
        public Task<bool> ExistsAsync(int id) => Task.FromResult(false);
        public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(_user is null ? [] : new List<User> { _user });
        public Task<bool> ExistsAsync(string email) => Task.FromResult(_user is not null && _user.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
    }
}
