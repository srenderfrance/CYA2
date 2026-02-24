using DataLibrary; // For IDataAccess
using Cya2.Application.DTOs;
using Cya2.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelsLibrary; // User entity

namespace Cya2.Application.Services;

/// <summary>
/// Simplified user management service for clean architecture
/// </summary>
public class UserManagementService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _config;
    private readonly IUserRepository _userRepository; // Clean architecture repository only
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IDataAccess dataAccess, 
        IConfiguration config, 
        IUserRepository userRepository,
        ILogger<UserManagementService> logger)
    {
        _dataAccess = dataAccess;
        _config = config;
        _userRepository = userRepository;
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
            // This would implement business logic to validate user access to specific accounts
            // For now, return true as a placeholder
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user access for user {UserId} and account {AccountFund}", userId, accountFund);
            return false;
        }
    }
}