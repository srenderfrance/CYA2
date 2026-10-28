using System.Security.Claims;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using cya2.Services;
using Xunit;

namespace Cya2.Web.Tests;

public sealed class AccountsOverviewAccessServiceTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AuthorizedAdminOrViewer_LoadsSharedOverview(bool isAdmin, bool isViewer)
    {
        var preload = new FakePreloadService();
        var service = CreateService(new UserAuthorizationResult(true, true, isAdmin, isViewer, 1, ""), preload);

        var result = await service.GetOverviewAsync(CreateUser());

        Assert.Same(preload.Overview, result);
        Assert.Equal(1, preload.AccountCalls);
        Assert.Equal(1, preload.OverviewCalls);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task UnauthorizedOrDemotedUser_DoesNotLoadOverview(bool isAuthorized, bool isAdmin, bool isViewer)
    {
        var preload = new FakePreloadService();
        var service = CreateService(new UserAuthorizationResult(isAuthorized, true, isAdmin, isViewer, 1, ""), preload);

        var result = await service.GetOverviewAsync(CreateUser());

        Assert.Null(result);
        Assert.Equal(0, preload.AccountCalls);
        Assert.Equal(0, preload.OverviewCalls);
    }

    [Fact]
    public async Task AnonymousUser_DoesNotLoadOverview()
    {
        var preload = new FakePreloadService();
        var service = CreateService(new UserAuthorizationResult(true, true, true, false, 1, ""), preload);

        var result = await service.GetOverviewAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.Null(result);
        Assert.Equal(0, preload.AccountCalls);
        Assert.Equal(0, preload.OverviewCalls);
    }

    [Fact]
    public async Task AuthorizationFailure_DoesNotLoadOverview()
    {
        var preload = new FakePreloadService();
        var service = new AccountsOverviewAccessService(new FakeUserIdResolver(), new FakeAuthorizationService(null), preload);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOverviewAsync(CreateUser()));
        Assert.Equal(0, preload.AccountCalls);
        Assert.Equal(0, preload.OverviewCalls);
    }

    private static AccountsOverviewAccessService CreateService(UserAuthorizationResult result, FakePreloadService preload) =>
        new(new FakeUserIdResolver(), new FakeAuthorizationService(result), preload);

    private static ClaimsPrincipal CreateUser() =>
        new(new ClaimsIdentity(new[] { new Claim("UserId", "1") }, "test"));

    private sealed class FakeUserIdResolver : IUserIdResolver
    {
        public string ResolveUserId(ClaimsPrincipal user, string? fallbackCurrentUserId = null) => "1";
    }

    private sealed class FakeAuthorizationService(UserAuthorizationResult? result) : IUserAuthorizationService
    {
        public Task<UserAuthorizationResult> ValidateUserAsync(string userIdentifier) =>
            result is null ? throw new InvalidOperationException("Database unavailable") : Task.FromResult(result);
        public Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, int accountId) => throw new NotImplementedException();
        public Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, string accountFund) => throw new NotImplementedException();
    }

    private sealed class FakePreloadService : IAdminPreloadService
    {
        public int AccountCalls { get; private set; }
        public int OverviewCalls { get; private set; }
        public IReadOnlyList<AdminAccountOverviewDto> Overview { get; } = new[] { new AdminAccountOverviewDto { Fund = "Test", CurrentBalance = 12m, Last12MonthsDonations = 34m } };
        public Task<AdminPreloadState> PreloadAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Account>> GetAccountsAsync(CancellationToken cancellationToken = default)
        {
            AccountCalls++;
            return Task.FromResult<IReadOnlyList<Account>>(Array.Empty<Account>());
        }
        public Task<IReadOnlyList<AdminAccountOverviewDto>> GetAccountOverviewAsync(CancellationToken cancellationToken = default)
        {
            OverviewCalls++;
            return Task.FromResult(Overview);
        }
        public Task<IReadOnlyList<SubAccount>> GetSubAccountsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AdminUserDto>> GetStaffAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public void Invalidate() => throw new NotImplementedException();
    }
}
