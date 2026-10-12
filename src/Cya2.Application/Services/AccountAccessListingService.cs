using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class AccountAccessListingService : IAccountAccessListingService
{
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly IUserRepository _userRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IAdminAuthorizationContext _adminAuthorizationContext;
    private readonly ILogger<AccountAccessListingService> _logger;

    public AccountAccessListingService(
        IUserAccountAccessRepository userAccountAccessRepository,
        IUserRepository userRepository,
        IAccountRepository accountRepository,
        IAdminAuthorizationContext adminAuthorizationContext,
        ILogger<AccountAccessListingService> logger)
    {
        _userAccountAccessRepository = userAccountAccessRepository;
        _userRepository = userRepository;
        _accountRepository = accountRepository;
        _adminAuthorizationContext = adminAuthorizationContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AccountAccessUserDto>> GetUsersWithAccessAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0 || !await _adminAuthorizationContext.IsCurrentUserAdminAsync())
        {
            return Array.Empty<AccountAccessUserDto>();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _accountRepository.GetByIdAsync(accountId) is null)
            {
                return Array.Empty<AccountAccessUserDto>();
            }

            var assignedUsers = await _userAccountAccessRepository.GetAccountUsersAsync(accountId);
            var globalUsers = await _userRepository.GetAllAsync();

            return assignedUsers
                .Concat(globalUsers.Where(user => user.CanViewAllAccounts()))
                .Where(user => user.Id > 0 && !string.IsNullOrWhiteSpace(user.Email) && !string.IsNullOrWhiteSpace(user.Name))
                .GroupBy(user => user.Id)
                .Select(group => group.First())
                .Select(user => new AccountAccessUserDto
                {
                    Id = user.Id,
                    Name = user.Name,
                    Email = user.Email,
                    AuthLevel = user.AuthLevel,
                    IsGlobalViewer = user.CanViewAllAccounts()
                })
                .OrderBy(user => user.Name)
                .ThenBy(user => user.Email)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with access to account {AccountId}", accountId);
            return Array.Empty<AccountAccessUserDto>();
        }
    }
}
