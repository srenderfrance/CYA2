using System.Globalization;
using System.Security.Claims;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using cya2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class SessionValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Admin")]
    [InlineData("User")]
    [InlineData("Viewer")]
    [InlineData("Intern")]
    public async Task CurrentUserAndRole_AreValid(string role)
    {
        var repository = new UserRepositoryStub { User = new User { Id = 7, AuthLevel = role } };
        Assert.Equal(SessionValidationResult.Valid, await Create(repository).ValidateAsync(Principal(role, Now.AddHours(4))));
    }

    [Fact]
    public async Task DeletedUser_IsRevoked()
    {
        Assert.Equal(SessionValidationResult.Revoked, await Create(new UserRepositoryStub()).ValidateAsync(Principal("Admin", Now.AddHours(4))));
    }

    [Theory]
    [InlineData("Admin", "User")]
    [InlineData("Viewer", "User")]
    [InlineData("User", "Admin")]
    public async Task ChangedRole_IsRevoked(string oldRole, string newRole)
    {
        var repository = new UserRepositoryStub { User = new User { Id = 7, AuthLevel = newRole } };
        Assert.Equal(SessionValidationResult.Revoked, await Create(repository).ValidateAsync(Principal(oldRole, Now.AddHours(4))));
    }

    [Fact]
    public async Task DatabaseFailure_IsRetriedWithoutTreatingUserAsDeleted()
    {
        var repository = new UserRepositoryStub { Throw = true, User = new User { Id = 7, AuthLevel = "Admin" } };
        var service = Create(repository);
        var principal = Principal("Admin", Now.AddHours(4));
        Assert.Equal(SessionValidationResult.Unavailable, await service.ValidateAsync(principal));
        repository.Throw = false;
        Assert.Equal(SessionValidationResult.Valid, await service.ValidateAsync(principal));
        repository.User = null;
        Assert.Equal(SessionValidationResult.Revoked, await service.ValidateAsync(principal));
    }

    [Fact]
    public async Task AbsoluteExpiry_IsEnforcedEvenWhenDatabaseIsUnavailable()
    {
        var repository = new UserRepositoryStub { Throw = true };
        Assert.Equal(SessionValidationResult.Expired, await Create(repository).ValidateAsync(Principal("Admin", Now)));
        Assert.Equal(0, repository.ReadCount);
    }

    [Fact]
    public async Task MissingExpiryOrUserId_IsRevoked()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("AuthLevel", "Admin")], "test"));
        Assert.Equal(SessionValidationResult.Revoked, await Create(new UserRepositoryStub()).ValidateAsync(principal));
    }

    private static SessionValidationService Create(UserRepositoryStub users) => new(users, new FixedClock(), NullLogger<SessionValidationService>.Instance);

    private static ClaimsPrincipal Principal(string role, DateTimeOffset expires) => new(new ClaimsIdentity([
        new Claim("UserId", "7"),
        new Claim("AuthLevel", role),
        new Claim(SessionValidationService.ExpiresClaim, expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))
    ], "test"));

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class UserRepositoryStub : IUserRepository
    {
        public User? User { get; set; }
        public bool Throw { get; set; }
        public int ReadCount { get; private set; }
        public Task<User?> GetByIdAsync(int id)
        {
            ReadCount++;
            if (Throw) throw new InvalidOperationException("Database unavailable");
            return Task.FromResult(User?.Id == id ? User : null);
        }
        public Task<User?> GetByEmailAsync(string email) => throw new NotImplementedException();
        public Task<User?> GetByExternalIdAsync(string externalId) => throw new NotImplementedException();
        public Task<List<User>> GetActiveUsersAsync() => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string email) => throw new NotImplementedException();
        public Task<List<User>> GetAllAsync() => throw new NotImplementedException();
        public Task<User> AddAsync(User entity) => throw new NotImplementedException();
        public Task<User> UpdateAsync(User entity) => throw new NotImplementedException();
        public Task DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(int id) => throw new NotImplementedException();
    }
}
