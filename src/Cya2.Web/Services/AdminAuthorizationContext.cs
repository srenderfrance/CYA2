using System.Security.Claims;
using Cya2.Application.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;

namespace cya2.Services;

public sealed class AdminAuthorizationContext : IAdminAuthorizationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IUserAuthorizationService _authorizationService;

    public AdminAuthorizationContext(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authenticationStateProvider,
        IUserAuthorizationService authorizationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authenticationStateProvider = authenticationStateProvider;
        _authorizationService = authorizationService;
    }

    public async Task<bool> IsCurrentUserAdminAsync()
    {
        return await GetCurrentAdminUserIdAsync() is not null;
    }

    public async Task<int?> GetCurrentAdminUserIdAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            principal = (await _authenticationStateProvider.GetAuthenticationStateAsync()).User;
        }

        var userIdentifier = principal?.FindFirstValue("UserId");
        if (!int.TryParse(userIdentifier, out var userId) || userId <= 0)
        {
            return null;
        }

        var authorization = await _authorizationService.ValidateUserAsync(userId.ToString());
        return authorization.IsAuthorized && authorization.IsAdmin ? userId : null;
    }
}
