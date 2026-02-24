using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for user management business logic
/// Moves logic from Admin.razor user management section
/// </summary>
public interface IUserManagementService
{
    /// <summary>
    /// Create new user with account assignments
    /// </summary>
    Task<UserOperationResult> CreateUserAsync(CreateUserRequest request);
    
    /// <summary>
    /// Update existing user information
    /// </summary>
    Task<UserOperationResult> UpdateUserAsync(UpdateUserRequest request);
    
    /// <summary>
    /// Delete user (with dependency checking)
    /// </summary>
    Task<UserOperationResult> DeleteUserAsync(int userId);
    
    /// <summary>
    /// Link account access to user
    /// </summary>
    Task<UserOperationResult> GrantAccountAccessAsync(int userId, int accountId);
    
    /// <summary>
    /// Remove account access from user
    /// </summary>
    Task<UserOperationResult> RevokeAccountAccessAsync(int userId, int accountId);
    
    /// <summary>
    /// Get all users for management
    /// </summary>
    Task<List<UserSummary>> GetAllUsersAsync();
    
    /// <summary>
    /// Get user details including account access
    /// </summary>
    Task<UserDetails?> GetUserDetailsAsync(int userId);
    
    /// <summary>
    /// Get accounts user has access to
    /// </summary>
    Task<List<Account>> GetUserAccountsAsync(int userId);
    
    /// <summary>
    /// Get accounts user does NOT have access to (for granting)
    /// </summary>
    Task<List<Account>> GetAvailableAccountsForUserAsync(int userId);
    
    /// <summary>
    /// Validate user data before save
    /// </summary>
    Task<ValidationResult> ValidateUserDataAsync(UserValidationRequest request);
    
    /// <summary>
    /// Check if user has dependencies before deletion
    /// </summary>
    Task<UserDependencyResult> CheckUserDependenciesAsync(int userId);
}

/// <summary>
/// Request to create new user
/// </summary>
public class CreateUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = "User"; // "User", "Viewer", "Admin"
    public List<int> AccountIds { get; set; } = new(); // Initial account assignments
    public string GoogleId { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Preference { get; set; } = "default";
}

/// <summary>
/// Request to update existing user
/// </summary>
public class UpdateUserRequest
{
    public int UserId { get; set; }
    public string? NewName { get; set; } // null = don't update
    public string? NewEmail { get; set; } // null = don't update
    public string? NewAuthLevel { get; set; } // null = don't update
    public string? NewLanguage { get; set; } // null = don't update
    public string? NewPreference { get; set; } // null = don't update
}

/// <summary>
/// Request for user validation
/// </summary>
public class UserValidationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = string.Empty;
    public int? ExistingUserId { get; set; } // For updates
}

/// <summary>
/// User summary for lists/dropdowns
/// </summary>
public class UserSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = string.Empty;
    public int AccountCount { get; set; }
    public DateTime DateCreated { get; set; }
}

/// <summary>
/// Detailed user information
/// </summary>
public class UserDetails
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Preference { get; set; } = string.Empty;
    public string GoogleId { get; set; } = string.Empty;
    public int? DefaultAccountId { get; set; }
    public DateTime DateCreated { get; set; }
    public List<Account> AccessibleAccounts { get; set; } = new();
}

/// <summary>
/// Result of user operation
/// </summary>
public class UserOperationResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public UserDetails? CreatedUser { get; set; }
}

/// <summary>
/// Result of user dependency check
/// </summary>
public class UserDependencyResult
{
    public bool HasDependencies { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public bool CanDelete { get; set; }
    public string DeleteBlockedReason { get; set; } = string.Empty;
}