using Cya2.Application.Interfaces;
using Cya2.Application.Services;
using Xunit;

namespace Cya2.Application.Tests;

public sealed class HomeAuthorizationServiceTests
{
    [Fact]
    public async Task AuthorizeAsync_RejectsDeletedUser()
    {
        var service = CreateService(null);

        var result = await service.AuthorizeAsync("deleted", "Fund A");

        Assert.False(result.IsAuthorized);
        Assert.Null(result.Context);
    }

    [Fact]
    public async Task AuthorizeAsync_ViewerUsesCurrentRoleAndCanSelectAnyAccount()
    {
        var account = Account(12, "Fund A");
        var service = CreateService(new UserAccountContext
        {
            UserId = 7,
            IsAdminOrViewer = true,
            IsViewer = true,
            Accounts = [account]
        });

        var result = await service.AuthorizeAsync("7", "Fund A");

        Assert.True(result.IsAuthorized);
        Assert.True(result.IsViewer);
        Assert.Equal("Fund A", result.SelectedAccount!.Fund);
    }

    [Fact]
    public async Task AuthorizeAsync_FallsBackWhenPreferredAccountWasRevoked()
    {
        var account = Account(12, "Current Fund");
        var service = CreateService(new UserAccountContext
        {
            UserId = 7,
            DefaultAccountId = 12,
            Accounts = [account]
        });

        var result = await service.AuthorizeAsync("7", "Revoked Fund");

        Assert.True(result.IsAuthorized);
        Assert.Equal("Current Fund", result.SelectedAccount!.Fund);
    }

    [Fact]
    public async Task AuthorizeAsync_RejectsRegularUserWithoutCurrentMemberships()
    {
        var service = CreateService(new UserAccountContext { UserId = 7 });

        var result = await service.AuthorizeAsync("7", null);

        Assert.False(result.IsAuthorized);
        Assert.Equal("User has no accessible accounts.", result.Reason);
    }

    private static HomeAuthorizationService CreateService(UserAccountContext? context) =>
        new(new FakeContextService(context));

    private static UserAccountContextAccount Account(int id, string fund) => new()
    {
        AccountId = id,
        Fund = fund,
        DisplayName = fund
    };

    private sealed class FakeContextService(UserAccountContext? context) : IUserAccountContextService
    {
        public Task<UserAccountContext?> GetContextAsync(string userId, bool isAdminOrViewerHint = false) =>
            Task.FromResult(context);

        public UserAccountContextAccount? ResolveSelectedAccount(UserAccountContext context, string? preferredFund)
        {
            var selected = context.Accounts.FirstOrDefault(a =>
                !string.IsNullOrWhiteSpace(preferredFund) &&
                string.Equals(a.Fund, preferredFund, StringComparison.OrdinalIgnoreCase));
            return selected
                ?? context.Accounts.FirstOrDefault(a => context.DefaultAccountId == a.AccountId)
                ?? context.Accounts.FirstOrDefault();
        }

        public void Invalidate(string userId) { }
    }
}
