using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class UserAuthorizationService : IUserAuthorizationService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly ILogger<UserAuthorizationService> _logger;

    public UserAuthorizationService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        ILogger<UserAuthorizationService> logger)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _logger = logger;
    }

    public async Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, string accountFund)
    {
        var user = await ValidateUserAsync(userIdentifier);
        if (!user.IsAuthorized || string.IsNullOrWhiteSpace(accountFund))
        {
            return user with { IsAuthorized = false, Reason = "User or account is invalid." };
        }

        if (user.CanAccessAllAccounts)
        {
            return user;
        }

        var account = await _userAccountAccessRepository.GetUserAccountsAsync(user.UserId);
        return account.Any(a => string.Equals(a.Fund, accountFund, StringComparison.OrdinalIgnoreCase))
            ? user
            : user with { IsAuthorized = false, Reason = "User is not assigned to this account." };
    }

    public async Task<UserAuthorizationResult> ValidateUserAsync(string userIdentifier)
    {
        var user = await ResolveUserAsync(userIdentifier);
        if (user is null)
        {
            return UserAuthorizationResult.Denied("User does not exist.");
        }

        var isAdmin = user.IsAdmin();
        var isViewer = user.IsViewer();
        return new UserAuthorizationResult(true, true, isAdmin, isViewer, user.Id, string.Empty);
    }

    public async Task<UserAuthorizationResult> ValidateAccountAccessAsync(string userIdentifier, int accountId)
    {
        var result = await ValidateUserAsync(userIdentifier);
        if (!result.IsAuthorized || accountId <= 0)
        {
            return result with { IsAuthorized = false, Reason = "User or account is invalid." };
        }

        if (result.CanAccessAllAccounts)
        {
            return result;
        }

        var hasAccess = await _userAccountAccessRepository.HasAccessAsync(result.UserId, accountId);
        return hasAccess
            ? result
            : result with { IsAuthorized = false, Reason = "User is not assigned to this account." };
    }

    private async Task<Cya2.Core.Entities.User?> ResolveUserAsync(string userIdentifier)
    {
        if (string.IsNullOrWhiteSpace(userIdentifier))
        {
            return null;
        }

        try
        {
            var normalized = userIdentifier.Trim();
            if (int.TryParse(normalized, out var userId))
            {
                var user = await _userRepository.GetByIdAsync(userId);
                if (user is not null)
                {
                    return user;
                }
            }

            var byEmail = await _userRepository.GetByEmailAsync(normalized);
            if (byEmail is not null)
            {
                return byEmail;
            }

            return await _userRepository.GetByExternalIdAsync(normalized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to revalidate user authorization for identifier {UserIdentifier}", userIdentifier);
            throw;
        }
    }
}
