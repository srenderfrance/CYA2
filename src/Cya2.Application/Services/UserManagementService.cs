using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

/// <summary>
/// Simplified user management service for clean architecture
/// </summary>
public class UserManagementService
{
    public const string OrganizationEmailDomain = "@scene8.net";

    public static bool IsOrganizationEmail(string? email)
    {
        var value = email?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 254 || value.Any(char.IsWhiteSpace))
            return false;

        var separator = value.IndexOf('@');
        if (separator <= 0 || separator > 64 || separator != value.LastIndexOf('@') ||
            !value[separator..].Equals(OrganizationEmailDomain, StringComparison.OrdinalIgnoreCase))
            return false;

        return System.Net.Mail.MailAddress.TryCreate(value, out var address) &&
               string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryBuildOrganizationEmail(string? username, out string email)
    {
        email = string.Empty;
        var localPart = username?.Trim();
        if (string.IsNullOrEmpty(localPart) || localPart.Contains('@'))
            return false;

        var candidate = localPart + OrganizationEmailDomain;
        if (!IsOrganizationEmail(candidate))
            return false;

        email = candidate;
        return true;
    }

    private readonly IUserRepository _userRepository;
    private readonly IUserAccountAccessRepository _userAccountAccessRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly IUserAdministrationStore _administrationStore;
    private readonly IAdminAuthorizationContext _adminAuthorizationContext;
    private readonly IImportCacheInvalidator? _cacheInvalidator;
    private readonly IAuditEventService? _auditEvents;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IUserRepository userRepository,
        IUserAccountAccessRepository userAccountAccessRepository,
        IAccountRepository accountRepository,
        IAdminAuthorizationContext adminAuthorizationContext,
        ILogger<UserManagementService> logger,
        IUserAdministrationStore administrationStore,
        IImportCacheInvalidator? cacheInvalidator = null,
        IAuditEventService? auditEvents = null)
    {
        _userRepository = userRepository;
        _userAccountAccessRepository = userAccountAccessRepository;
        _accountRepository = accountRepository;
        _adminAuthorizationContext = adminAuthorizationContext;
        _logger = logger;
        _administrationStore = administrationStore;
        _cacheInvalidator = cacheInvalidator;
        _auditEvents = auditEvents;
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
            _logger.LogError("Error getting user by email. ExceptionType={ExceptionType}", ex.GetType().Name);
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
            _logger.LogError("Error getting active users. ExceptionType={ExceptionType}", ex.GetType().Name);
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
            _logger.LogError("Error validating user access. ExceptionType={ExceptionType}", ex.GetType().Name);
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
            var actorUserId = await _adminAuthorizationContext.GetCurrentAdminUserIdAsync();
            if (actorUserId is null)
            {
                throw new UnauthorizedAccessException("Administrator access is required.");
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
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting admin users list. ExceptionType={ExceptionType}", ex.GetType().Name);
            throw new InvalidOperationException("Unable to load the administrator user list.");
        }
    }

    public Task<bool> IsCurrentUserAdminAsync() => _adminAuthorizationContext.IsCurrentUserAdminAsync();

    public async Task<AdminUserOperationDto> UpdateAdminUserAsync(AdminUserUpdateDto request)
    {
        using var operation = AuditOperationScope.Begin(null, targetUserId: request.UserId);
        int? actorUserId = null;
        var attempted = false;
        var committed = false;
        try
        {
            actorUserId = await _adminAuthorizationContext.GetCurrentAdminUserIdAsync();
            if (actorUserId is null)
            {
                await RecordUserOutcomeAsync(null, AuditOutcomes.Denied);
                return NotAuthorized();
            }

            if (request.UserId <= 0)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "Invalid user id." };
            }

            var profileChanged = !string.IsNullOrWhiteSpace(request.Name) ||
                !string.IsNullOrWhiteSpace(request.Email) || !string.IsNullOrWhiteSpace(request.AuthLevel);
            if (!profileChanged && request.GrantAccountId is null && request.RevokeAccountId is null)
            {
                return new AdminUserOperationDto { IsSuccess = false, Message = "No changes provided for update." };
            }

            if (profileChanged)
            {
                var user = await _userRepository.GetByIdAsync(request.UserId);
                if (user == null)
                    return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };
                var targetEmail = string.IsNullOrWhiteSpace(request.Email) ? user.Email : request.Email.Trim();
                if (!IsOrganizationEmail(targetEmail))
                    return new AdminUserOperationDto { IsSuccess = false, Message = "A valid @scene8.net email is required." };
            }

            attempted = true;
            var result = await _administrationStore.UpdateAsync(new UpdateUserCommand(actorUserId.Value, request.UserId,
                request.Name, request.Email, request.AuthLevel, request.GrantAccountId, request.RevokeAccountId, request.ReuseExistingInternAccount));
            if (result.Status != UserAdministrationStatus.Success)
            {
                await RecordUserOutcomeAsync(actorUserId, AuditOutcomes.Denied);
                return FromAdministrationStatus(result.Status);
            }
            committed = true;

            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization |
                (result.InternAccountChanged && profileChanged ? CacheDataDomains.AccountMetadata : CacheDataDomains.None));

            return new AdminUserOperationDto { IsSuccess = true, Message = "User updated successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error updating admin user. ExceptionType={ExceptionType}", ex.GetType().Name);
            if (!committed) await RecordUserOutcomeAsync(actorUserId, attempted && ex is not AuditPersistenceException ? AuditOutcomes.Unknown : AuditOutcomes.Failed);
            return HandleMutationFailure(attempted, committed, CacheDataDomains.Authorization | CacheDataDomains.AccountMetadata);
        }
    }

    public async Task<List<Cya2.Core.Entities.Account>> GetUserLinkedAccountsAsync(int userId)
    {
        try
        {
            var actorUserId = await _adminAuthorizationContext.GetCurrentAdminUserIdAsync();
            if (actorUserId is null)
            {
                return new List<Cya2.Core.Entities.Account>();
            }

            return await _userAccountAccessRepository.GetUserAccountsAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error loading linked accounts. ExceptionType={ExceptionType}", ex.GetType().Name);
            return new List<Cya2.Core.Entities.Account>();
        }
    }

    public Task<AdminUserOperationDto> GrantAccountAccessAsync(int userId, int accountId) =>
        UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = userId, GrantAccountId = accountId });

    public Task<AdminUserOperationDto> RevokeAccountAccessAsync(int userId, int accountId) =>
        UpdateAdminUserAsync(new AdminUserUpdateDto { UserId = userId, RevokeAccountId = accountId });

    public async Task<AdminUserOperationDto> CreateAdminUserAsync(string name, string email, string authLevel, IEnumerable<int> accountIds, bool reuseExistingInternAccount = false)
    {
        using var operation = AuditOperationScope.Begin(null);
        int? actorUserId = null;
        var attempted = false;
        var committed = false;
        try
        {
            actorUserId = await _adminAuthorizationContext.GetCurrentAdminUserIdAsync();
            if (actorUserId is null)
            {
                await RecordUserOutcomeAsync(null, AuditOutcomes.Denied);
                return NotAuthorized();
            }

            if (string.IsNullOrWhiteSpace(name))
                return new AdminUserOperationDto { IsSuccess = false, Message = "Name is required" };

            if (!IsOrganizationEmail(email))
                return new AdminUserOperationDto { IsSuccess = false, Message = "A valid @scene8.net email is required." };

            attempted = true;
            var result = await _administrationStore.CreateAsync(new CreateUserCommand(actorUserId.Value, name.Trim(), email.Trim(),
                string.IsNullOrWhiteSpace(authLevel) ? "User" : authLevel.Trim(),
                (accountIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToArray(), reuseExistingInternAccount));
            if (result.Status != UserAdministrationStatus.Success)
            {
                await RecordUserOutcomeAsync(actorUserId, AuditOutcomes.Denied);
                return FromAdministrationStatus(result.Status);
            }
            committed = true;

            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization |
                (result.InternAccountChanged ? CacheDataDomains.AccountMetadata : CacheDataDomains.None));

            return new AdminUserOperationDto
            {
                IsSuccess = true,
                Message = "New user registered successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error creating admin user. ExceptionType={ExceptionType}", ex.GetType().Name);
            if (!committed) await RecordUserOutcomeAsync(actorUserId, attempted && ex is not AuditPersistenceException ? AuditOutcomes.Unknown : AuditOutcomes.Failed);
            return HandleMutationFailure(attempted, committed, CacheDataDomains.Authorization | CacheDataDomains.AccountMetadata);
        }
    }

    public async Task<AdminUserOperationDto> DeleteAdminUserAsync(int userId)
    {
        using var operation = AuditOperationScope.Begin(null, targetUserId: userId);
        int? actorUserId = null;
        var attempted = false;
        var committed = false;
        try
        {
            actorUserId = await _adminAuthorizationContext.GetCurrentAdminUserIdAsync();
            if (actorUserId is null)
            {
                await RecordUserOutcomeAsync(null, AuditOutcomes.Denied);
                return NotAuthorized();
            }

            if (userId <= 0)
                return new AdminUserOperationDto { IsSuccess = false, Message = "Selected user not found" };

            attempted = true;
            var result = await _administrationStore.DeleteAsync(actorUserId.Value, userId);
            if (result.Status != UserAdministrationStatus.Success)
            {
                await RecordUserOutcomeAsync(actorUserId, AuditOutcomes.Denied);
                return FromAdministrationStatus(result.Status);
            }
            committed = true;
            _cacheInvalidator?.Invalidate(CacheDataDomains.Authorization);

            return new AdminUserOperationDto { IsSuccess = true, Message = "User deleted successfully" };
        }
        catch (Exception ex)
        {
            _logger.LogError("Error deleting user. ExceptionType={ExceptionType}", ex.GetType().Name);
            if (!committed) await RecordUserOutcomeAsync(actorUserId, attempted && ex is not AuditPersistenceException ? AuditOutcomes.Unknown : AuditOutcomes.Failed);
            return HandleMutationFailure(attempted, committed, CacheDataDomains.Authorization);
        }
    }

    private AdminUserOperationDto HandleMutationFailure(bool attempted, bool committed, CacheDataDomains domains)
    {
        if (attempted)
        {
            try { _cacheInvalidator?.Invalidate(domains); }
            catch (Exception ex) { _logger.LogError("Unable to invalidate user administration caches. ExceptionType={ExceptionType}", ex.GetType().Name); }
        }

        return new AdminUserOperationDto
        {
            IsSuccess = committed,
            Message = committed ? "The change was saved, but follow-up processing failed. Refresh and verify the result." :
                attempted ? "The result could not be confirmed. Refresh and verify before retrying." :
                "Unable to process the user change. Please try again."
        };
    }

    private static AdminUserOperationDto NotAuthorized()
        => new() { IsSuccess = false, Message = "Administrator authorization is required." };

    private static AdminUserOperationDto FromAdministrationStatus(UserAdministrationStatus status) => new()
    {
        RequiresInternAccountReuse = status == UserAdministrationStatus.InternReuseRequired,
        Message = status switch
        {
            UserAdministrationStatus.NotAuthorized => "Administrator authorization is required.",
            UserAdministrationStatus.NotFound => "Selected user not found",
            UserAdministrationStatus.SelfChange => "Administrators cannot delete their own user or remove their own administrator role.",
            UserAdministrationStatus.LastAdmin => "The last administrator cannot be deleted or demoted.",
            UserAdministrationStatus.DuplicateEmail => "A user with this email already exists",
            UserAdministrationStatus.InternRoleChange => "An intern's role cannot be changed. Create a new user for a different role.",
            UserAdministrationStatus.InternReuseRequired => "An intern account already exists for this name. Reuse the existing account or modify the user name.",
            UserAdministrationStatus.InvalidAccount => "The selected account is invalid or cannot be both added and removed.",
            UserAdministrationStatus.AccountAlreadyLinked => "The selected account is already linked to this user.",
            UserAdministrationStatus.AccountNotLinked => "The selected account is not linked to this user.",
            _ => "Unable to update the user. Please try again."
        }
    };

    private async Task RecordUserOutcomeAsync(int? actorUserId, string outcome)
    {
        try
        {
            if (_auditEvents is not null)
                await _auditEvents.RecordAsync(actorUserId,
                    outcome == AuditOutcomes.Denied ? AuditEventTypes.AdminUserChangeDenied : outcome == AuditOutcomes.Unknown ? AuditEventTypes.AdminUserChangeUnconfirmed : AuditEventTypes.AdminUserChangeFailed,
                    outcome, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(new EventId(9100, "AuditWriteFailed"), "Administration audit delivery failed. OperationId={OperationId} ExceptionType={ExceptionType}", AuditOperationScope.Current?.OperationId, ex.GetType().Name);
        }
    }

}