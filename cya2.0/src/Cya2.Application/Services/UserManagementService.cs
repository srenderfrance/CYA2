using DataLibrary; // Main application's IDataAccess
using Cya2.Application.DTOs;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UserAuth; // Main application's UserRepository
using ModelsLibrary; // User entity

namespace Cya2.Application.Services;

/// <summary>
/// User management service that bridges external user repository with clean architecture
/// </summary>
public class UserManagementService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _config;
    private readonly UserRepository _externalUserRepository; // Main application's UserRepository
    private readonly IUserRepository _coreUserRepository; // Clean architecture repository
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IDataAccess dataAccess, 
        IConfiguration config, 
        UserRepository externalUserRepository, // Use concrete type from main app
        IUserRepository coreUserRepository,
        ILogger<UserManagementService> logger)
    {
        _dataAccess = dataAccess;
        _config = config;
        _externalUserRepository = externalUserRepository;
        _coreUserRepository = coreUserRepository;
        _logger = logger;
    }

    public async Task<UserDto?> GetUserByEmailAsync(string email)
    {
        try
        {
            var externalUser = await _externalUserRepository.GetUserByEmailAsync(email);
            if (externalUser == null) return null;

            return new UserDto
            {
                Id = externalUser.Id.ToString(),
                Email = externalUser.Email ?? "",
                Name = externalUser.Name ?? "",
                IsActive = true,
                CreatedAt = externalUser.DateCreated
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
            var coreUsers = await _coreUserRepository.GetActiveUsersAsync();
            return coreUsers.Select(u => new UserDto
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
            // This would implement business logic to validate user access to specific accounts
            // For now, return true as a placeholder
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access for user {UserId} and account {AccountFund}", userId, accountFund);
            return false;
        }
    }
}