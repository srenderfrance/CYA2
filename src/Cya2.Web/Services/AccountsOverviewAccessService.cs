using System.Security.Claims;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;

namespace cya2.Services;

public sealed class AccountsOverviewAccessService(
    IUserIdResolver userIdResolver,
    IUserAuthorizationService userAuthorizationService,
    IAdminPreloadService adminPreloadService)
{
    public async Task<IReadOnlyList<AdminAccountOverviewDto>?> GetOverviewAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userId = userIdResolver.ResolveUserId(user);
        var authorization = await userAuthorizationService.ValidateUserAsync(userId);
        if (!authorization.IsAuthorized || !authorization.CanAccessAllAccounts)
        {
            return null;
        }

        await adminPreloadService.GetAccountsAsync();
        return await adminPreloadService.GetAccountOverviewAsync();
    }
}
