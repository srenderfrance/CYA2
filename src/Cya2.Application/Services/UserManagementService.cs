using Cya2.Application.DTOs;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Simplified user management service for clean architecture
/// </summary>
public class UserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        ILogger<UserManagementService> logger)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _logger = logger;
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
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access for user {UserId} and account {AccountFund}", userId, accountFund);
            return false;
        }
    }

    public async Task<List<AdminUserDto>> GetAdminUsersAsync()
    {
        try
        {
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

            if (!string.IsNullOrWhiteSpace(request.Name)) user.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.Email)) user.Email = request.Email.Trim();
            if (!string.IsNullOrWhiteSpace(request.AuthLevel)) user.AuthLevel = request.AuthLevel.Trim();

            await _userRepository.UpdateAsync(user);

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
            if (userId <= 0) return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };
            if (accountId <= 0) return new AdminUserOperationDto { IsSuccess = false, Message = "Please select an account to remove" };

            var account = await _userAccountAccessRepository.GetAccountByIdAsync(accountId);
            var accountName = account?.Fund ?? "Unknown";

            var revoked = await _userAccountAccessRepository.RevokeAccessAsync(userId, accountId);
            if (!revoked)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Failed to remove account link" };
            }

            return new AdminUserOperationDto { IsSuccess = true, Message = $"Successfully removed account '{accountName}'" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking account access. UserId={UserId}, AccountId={AccountId}", userId, accountId);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error removing account link: {ex.Message}" };
        }
    }

    public async Task<AdminUserOperationDto> CreateAdminUserAsync(string name, string email, string authLevel, IEnumerable<int> accountIds)
    {
        try
        {
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
            foreach (var accountId in realAccountIds)
            {
                await _userAccountAccessRepository.GrantAccessAsync(user.Id, accountId);
            }

            if (realAccountIds.Count == 1)
            {
                await _userAccountAccessRepository.SetUserDefaultAccountAsync(user.Id, realAccountIds[0]);
            }

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
            if (userId <= 0)
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };

            var exists = await _userRepository.ExistsAsync(userId);
            if (!exists)
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };

            await _userAccountAccessRepository.RevokeAllAccessAsync(userId);
            await _userRepository.DeleteAsync(userId);

            return new AdminUserOperationDto { IsSuccess = true, Message = "User deleted successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return new AdminUserOperationDto { IsSuccess = false, Message = $"Error: {ex.Message}" };
        }
    }
}