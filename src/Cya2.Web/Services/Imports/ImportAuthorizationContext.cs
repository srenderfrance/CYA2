using System.Security.Claims;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.AspNetCore.Components.Authorization;

namespace cya2.Services.Imports;

public sealed class ImportAuthorizationContext : IImportAuthorizationContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserRepository _userRepository;
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public ImportAuthorizationContext(
        IHttpContextAccessor httpContextAccessor,
        IUserRepository userRepository,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _userRepository = userRepository;
        _authenticationStateProvider = authenticationStateProvider;
    }

    public async Task<ImportActor?> GetCurrentActorAsync()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            principal = (await _authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var userIdValue = principal?.FindFirstValue("UserId");
        if (!int.TryParse(userIdValue, out var userId) || userId <= 0)
            return null;

        var user = await _userRepository.GetByIdAsync(userId);
        if (user is null || !user.IsAdmin())
            return null;

        return new ImportActor(user.Id.ToString(), true);
    }
}