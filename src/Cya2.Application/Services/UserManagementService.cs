using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Simplified user management service for clean architecture
/// </summary>
public class UserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IAdminAuthorizationContext _adminAuthorizationContext;
    private readonly IImportCacheInvalidator? _cacheInvalidator;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        IAccountRepository accountRepository,
        IAdminAuthorizationContext adminAuthorizationContext,
        ILogger<UserManagementService> logger,
        IImportCacheInvalidator? cacheInvalidator = null)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _accountRepository = accountRepository;
        _adminAuthorizationContext = adminAuthorizationContext;
        _logger = logger;
        _cacheInvalidator = cacheInvalidator;
    }

    public async Task<UserDto?> GetUserByEmailAsync(string email)
    {
        try
        {
            var user = await _userRepository.GetByEmailAsync(email);
            if (user == null) return null;

            return new UserDto
            {
                Id = user.Id.ToString(),
                Email = user.Email ?? "",
                Name = user.Name ?? "",
                IsActive = true,
                CreatedAt = user.DateCreated
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user by email: {Email}", email);
            return null;
        }
    }

    public async Task<List<UserDto>> GetActiveUsersAsync()
    {
        try
        {
            var users = await _userRepository.GetActiveUsersAsync();
            return users.Select(u => new UserDto
            {
                Id = u.Id.ToString(),
                Email = u.Email ?? "",
                Name = u.Name ?? "",
                IsActive = true,
                CreatedAt = u.DateCreated
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active users");
            return new List<UserDto>();
        }
    }

    public async Task<bool> ValidateUserAccessAsync(string userId, string accountFund)
    {
        try
        {
            var user = await ResolveUserAsync(userId);
            if (user is null)
            {
                return false;
            }

            if (user.CanViewAllAccounts())
            {
                return true;
            }

            var account = await _accountRepository.GetByFundAsync(accountFund);
            return account is not null && await _userAccountAccessRepository.HasAccessAsync(user.Id, account.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access for user {UserId}", userId);
            return false;
        }
    }

    private async Task<Cya2.Core.Entities.User?> ResolveUserAsync(string userId)
    {
        if (int.TryParse(userId, out var parsedUserId))
        {
            var user = await _userRepository.GetByIdAsync(parsedUserId);
            if (user is not null) return user;
        }

        var byEmail = await _userRepository.GetByEmailAsync(userId);
        return byEmail ?? await _userRepository.GetByExternalIdAsync(userId);
    }

    public async Task<List<AdminUserDto>> GetAdminUsersAsync()
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return new List<AdminUserDto>();
            }

            _logger.LogInformation("UserManagementService.GetAdminUsersAsync: requesting all users from repository.");
            var users = await _userRepository.GetAllAsync();
            _logger.LogInformation("UserManagementService.GetAdminUsersAsync: repository returned {Count} users.", users?.Count ?? 0);

            var mapped = (users ?? new List<Cya2.Core.Entities.User>()).Select(u => new AdminUserDto
            {
                Id = u.Id,
                Email = u.Email ?? string.Empty,
                Name = u.Name ?? string.Empty,
                Language = u.Language ?? string.Empty,
                AuthLevel = u.AuthLevel ?? string.Empty,
                DefaultAccountId = u.DefaultAccount
            }).OrderBy(u => u.Name).ToList();

            _logger.LogInformation("UserManagementService.GetAdminUsersAsync: mapped {Count} users.", mapped.Count);
            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin users list");
            return new List<AdminUserDto>();
        }
    }

    public async Task<AdminUserOperationDto> UpdateAdminUserAsync(AdminUserUpdateDto request)
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return NotAuthorized();
            }

            if (request.UserId <= 0)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Invalid user id." };
            }

            if (string.IsNullOrWhiteSpace(request.Name) &&
                string.IsNullOrWhiteSpace(request.Email) &&
                string.IsNullOrWhiteSpace(request.AuthLevel))
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "No changes provided for update." };
            }

            var user = await _userRepository.GetByIdAsync(request.UserId);
            if (user == null)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };
            }

            var originalName = user.Name;
            var wasIntern = user.IsIntern();
            var targetName = string.IsNullOrWhiteSpace(request.Name) ? user.Name : request.Name.Trim();
            var targetAuthLevel = string.IsNullOrWhiteSpace(request.AuthLevel) ? user.AuthLevel : request.AuthLevel.Trim();

            if (string.Equals(targetAuthLevel, "Intern", StringComparison.OrdinalIgnoreCase))
            {
                var desiredFund = InternAccountUtility.BuildInternFund(targetName);
                var existingAccount = await _accountRepository.GetByFundAsync(desiredFund);
                var linkedInternAccount = (await _userAccountAccessRepository.GetUserAccountsAsync(user.Id))
                    .FirstOrDefault(account => InternAccountUtility.IsInternFund(account.Fund));
                if (existingAccount != null && existingAccount.AccountId != linkedInternAccount?.AccountId && !request.ReuseExistingInternAccount)
                {
                    return new AdminUserOperationDto
                    {
                        RequiresInternAccountReuse = true,
                        Message = "An intern account already exists for this name. Reuse the existing account or modify the user name."
                    };
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Name)) user.Name = targetName;
            if (!string.IsNullOrWhiteSpace(request.Email)) user.Email = request.Email.Trim();
            if (!string.IsNullOrWhiteSpace(request.AuthLevel)) user.AuthLevel = targetAuthLevel;

            await _userRepository.UpdateAsync(user);

            if (string.Equals(user.AuthLevel, "Intern", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureInternAccountAccessAsync(user, originalName, request.ReuseExistingInternAccount);
            }
            else if (wasIntern)
            {
                var linkedAccounts = await _userAccountAccessRepository.GetUserAccountsAsync(user.Id);
                foreach (var internAccount in linkedAccounts.Where(account => InternAccountUtility.IsInternFund(account.Fund)))
                {
                    await _userAccountAccessRepository.RevokeAccessAsync(user.Id, internAccount.AccountId);
                }
            }

            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization);

            return new AdminUserOperationDto { IsSuccess = true, Message = "User updated successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating admin user {UserId}", request.UserId);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error updating user: {ex.Message}" };
        }
    }

    public async Task<List<Cya2.Core.Entities.Account>> GetUserLinkedAccountsAsync(int userId)
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return new List<Cya2.Core.Entities.Account>();
            }

            return await _userAccountAccessRepository.GetUserAccountsAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading linked accounts for user {UserId}", userId);
            return new List<Cya2.Core.Entities.Account>();
        }
    }

    public async Task<AdminUserOperationDto> GrantAccountAccessAsync(int userId, int accountId)
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return NotAuthorized();
            }

            if (userId <= 0) return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };
            if (accountId <= 0) return new AdminUserOperationDto { IsSuccess = false, Message = "Selected account not found" };

            var account = await _userAccountAccessRepository.GetAccountByIdAsync(accountId);
            if (account == null)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected account not found" };
            }

            if (await _userAccountAccessRepository.HasAccessAsync(userId, accountId))
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = $"Account '{account.Fund}' is already linked to this user" };
            }

            var granted = await _userAccountAccessRepository.GrantAccessAsync(userId, accountId);
            if (!granted)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Failed to link account" };
            }

            var count = await _userAccountAccessRepository.GetUserAccountCountAsync(userId);
            if (count == 1)
            {
                await _userAccountAccessRepository.SetUserDefaultAccountAsync(userId, accountId);
            }

            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization);

            return new AdminUserOperationDto { IsSuccess = true, Message = $"Successfully linked account '{account.Fund}'" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error granting account access. UserId={UserId}, AccountId={AccountId}", userId, accountId);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error linking account: {ex.Message}" };
        }
    }

    public async Task<AdminUserOperationDto> RevokeAccountAccessAsync(int userId, int accountId)
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return NotAuthorized();
            }

            if (userId <= 0) return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };
            if (accountId <= 0) return new AdminUserOperationDto { IsSuccess = false, Message = "Please select an account to remove" };

            var account = await _userAccountAccessRepository.GetAccountByIdAsync(accountId);
            var accountName = account?.Fund ?? "Unknown";

            var revoked = await _userAccountAccessRepository.RevokeAccessAsync(userId, accountId);
            if (!revoked)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Failed to remove account link" };
            }

            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization);

            return new AdminUserOperationDto { IsSuccess = true, Message = $"Successfully removed account '{accountName}'" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking account access. UserId={UserId}, AccountId={AccountId}", userId, accountId);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error removing account link: {ex.Message}" };
        }
    }

    public async Task<AdminUserOperationDto> CreateAdminUserAsync(string name, string email, string authLevel, IEnumerable<int> accountIds, bool reuseExistingInternAccount = false)
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return NotAuthorized();
            }

            if (string.IsNullOrWhiteSpace(name))
                return new AdminUserOperationDto { IsSuccess = false, Message = "Name is required" };

            if (string.IsNullOrWhiteSpace(email))
                return new AdminUserOperationDto { IsSuccess = false, Message = "Email is required" };

            try
            {
                var parsed = new System.Net.Mail.MailAddress(email);
                if (!string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return new AdminUserOperationDto { IsSuccess = false, Message = "Please enter a valid email address" };
                }
            }
            catch
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Please enter a valid email address" };
            }

            if (await _userRepository.ExistsAsync(email.Trim()))
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "A user with this email already exists" };
            }

            var isInternUser = string.Equals(authLevel, "Intern", StringComparison.OrdinalIgnoreCase);
            if (isInternUser && !reuseExistingInternAccount)
            {
                var existingAccount = await _accountRepository.GetByFundAsync(InternAccountUtility.BuildInternFund(name));
                if (existingAccount != null)
                {
                    return new AdminUserOperationDto
                    {
                        RequiresInternAccountReuse = true,
                        Message = "An intern account already exists for this name. Reuse the existing account or modify the user name."
                    };
                }
            }

            var user = await _userRepository.AddAsync(new Cya2.Core.Entities.User(
                googleId: string.Empty,
                email: email.Trim(),
                name: name.Trim(),
                language: "en",
                authLevel: string.IsNullOrWhiteSpace(authLevel) ? "User" : authLevel.Trim(),
                defaultAccount: null));

            if (user == null || user.Id <= 0)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Failed to create user" };
            }

            var realAccountIds = (accountIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToList();
            isInternUser = string.Equals(user.AuthLevel, "Intern", StringComparison.OrdinalIgnoreCase);

            if (isInternUser)
            {
                var internAccount = await EnsureInternAccountAccessAsync(user, reuseExistingAccount: reuseExistingInternAccount);
                realAccountIds = internAccount?.AccountId > 0
                    ? new List<int> { internAccount.AccountId }
                    : new List<int>();
            }

            foreach (var accountId in isInternUser ? Enumerable.Empty<int>() : realAccountIds)
            {
                await _userAccountAccessRepository.GrantAccessAsync(user.Id, accountId);
            }

            if (realAccountIds.Count == 1)
            {
                await _userAccountAccessRepository.SetUserDefaultAccountAsync(user.Id, realAccountIds[0]);
            }

            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization);

            return new AdminUserOperationDto
            {
                IsSuccess = true,
                Message = $"New user {user.Name} registered successfully with email {user.Email}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating admin user for email {Email}", email);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error: {ex.Message}" };
        }
    }

    public async Task<AdminUserOperationDto> DeleteAdminUserAsync(int userId)
    {
        try
        {
            if (!await _adminAuthorizationContext.IsCurrentUserAdminAsync())
            {
                return NotAuthorized();
            }

            if (userId <= 0)
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };

            var exists = await _userRepository.ExistsAsync(userId);
            if (!exists)
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };

            await _userAccountAccessRepository.RevokeAllAccessAsync(userId);
            await _userRepository.DeleteAsync(userId);
            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization);

            return new AdminUserOperationDto { IsSuccess = true, Message = "User deleted successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error: {ex.Message}" };
        }
    }

    private static AdminUserOperationDto NotAuthorized()
        => new() { IsSuccess = false, Message = "Administrator authorization is required." };

    private async Task<Cya2.Core.Entities.Account?> EnsureInternAccountAccessAsync(Cya2.Core.Entities.User user, string? originalName = null, bool reuseExistingAccount = false)
    {
        if (user == null || user.Id <= 0)
        {
            return null;
        }

        var desiredInternFund = InternAccountUtility.BuildInternFund(user.Name);
        var desiredAccount = await _accountRepository.GetByFundAsync(desiredInternFund);

        var linkedAccounts = await _userAccountAccessRepository.GetUserAccountsAsync(user.Id);
        var linkedInternAccount = linkedAccounts.FirstOrDefault(a => InternAccountUtility.IsInternFund(a.Fund));

        if (desiredAccount != null && linkedInternAccount != null && desiredAccount.AccountId != linkedInternAccount.AccountId && reuseExistingAccount)
        {
            await _userAccountAccessRepository.RevokeAccessAsync(user.Id, linkedInternAccount.AccountId);
            linkedInternAccount = null;
        }

        if (desiredAccount == null && linkedInternAccount != null)
        {
            linkedInternAccount.Fund = desiredInternFund;
            desiredAccount = await _accountRepository.UpdateAsync(linkedInternAccount);
        }

        if (desiredAccount == null && !string.IsNullOrWhiteSpace(originalName))
        {
            var previousFund = InternAccountUtility.BuildInternFund(originalName);
            var previousAccount = await _accountRepository.GetByFundAsync(previousFund);
            if (previousAccount != null)
            {
                previousAccount.Fund = desiredInternFund;
                desiredAccount = await _accountRepository.UpdateAsync(previousAccount);
            }
        }

        if (desiredAccount == null)
        {
            desiredAccount = await _accountRepository.AddAsync(new Cya2.Core.Entities.Account
            {
                Fund = desiredInternFund,
                AccountingClass = null,
                AccountNumber = $"INT-{user.Id}",
                Overhead = 12.00m,
                SoftCredit = string.Empty,
                BalanceAdjustment = 0,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (desiredAccount?.AccountId > 0)
        {
            var hasAccess = await _userAccountAccessRepository.HasAccessAsync(user.Id, desiredAccount.AccountId);
            if (!hasAccess)
            {
                await _userAccountAccessRepository.GrantAccessAsync(user.Id, desiredAccount.AccountId);
            }

            await _userAccountAccessRepository.SetUserDefaultAccountAsync(user.Id, desiredAccount.AccountId);
            return desiredAccount;
        }

        _logger.LogWarning("Failed to resolve/create intern account for user {UserId} ({Email})", user.Id, user.Email);
        return null;
    }
}