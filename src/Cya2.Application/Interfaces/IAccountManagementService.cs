using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for managing accounts and sub-accounts business logic
/// Moves logic from Admin.razor component
/// </summary>
public interface IAccountManagementService
{
    /// <summary>
    /// Create new primary fundraising account
    /// </summary>
    Task<AccountOperationResult> CreateAccountAsync(CreateAccountRequest request);
    
    /// <summary>
    /// Update existing account
    /// </summary>
    Task<AccountOperationResult> UpdateAccountAsync(UpdateAccountRequest request);
    
    /// <summary>
    /// Delete account (with validation for dependencies)
    /// </summary>
    Task<AccountOperationResult> DeleteAccountAsync(int accountId);
    
    /// <summary>
    /// Create new sub-account
    /// </summary>
    Task<SubAccountOperationResult> CreateSubAccountAsync(CreateSubAccountRequest request);
    
    /// <summary>
    /// Update existing sub-account
    /// </summary>
    Task<SubAccountOperationResult> UpdateSubAccountAsync(UpdateSubAccountRequest request);
    
    /// <summary>
    /// Delete sub-account
    /// </summary>
    Task<SubAccountOperationResult> DeleteSubAccountAsync(int subAccountId);
    
    /// <summary>
    /// Get all accounts for dropdown/selection
    /// </summary>
    Task<List<Account>> GetAllAccountsAsync();
    
    /// <summary>
    /// Get sub-accounts for specific primary account
    /// </summary>
    Task<List<SubAccount>> GetSubAccountsAsync(int primaryAccountId);
    
    /// <summary>
    /// Validate account data before save
    /// </summary>
    Task<ValidationResult> ValidateAccountDataAsync(AccountValidationRequest request);
    
    /// <summary>
    /// Check if account has any dependencies (donations, users, etc.)
    /// </summary>
    Task<AccountDependencyResult> CheckAccountDependenciesAsync(int accountId);
}

/// <summary>
/// Request to create new account
/// </summary>
public class CreateAccountRequest
{
    public string Fund { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public decimal Overhead { get; set; }
    public string SoftCredit { get; set; } = string.Empty;
    public decimal BalanceAdjustment { get; set; }
}

/// <summary>
/// Request to update existing account
/// </summary>
public class UpdateAccountRequest : CreateAccountRequest
{
    public int AccountId { get; set; }
}

/// <summary>
/// Request to create sub-account
/// </summary>
public class CreateSubAccountRequest
{
    public int PrimaryAccountId { get; set; }
    public string SubFund { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty; // "Merged" or "Separate"
}

/// <summary>
/// Request to update sub-account
/// </summary>
public class UpdateSubAccountRequest : CreateSubAccountRequest
{
    public int SubAccountId { get; set; }
}

/// <summary>
/// Request for account validation
/// </summary>
public class AccountValidationRequest
{
    public string Fund { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public int? ExistingAccountId { get; set; } // For updates
}

/// <summary>
/// Result of account operation
/// </summary>
public class AccountOperationResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public Account? CreatedAccount { get; set; }
}

/// <summary>
/// Result of sub-account operation
/// </summary>
public class SubAccountOperationResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public SubAccount? CreatedSubAccount { get; set; }
}

/// <summary>
/// Result of validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Result of dependency check
/// </summary>
public class AccountDependencyResult
{
    public bool HasDependencies { get; set; }
    public List<string> Dependencies { get; set; } = new();
    public bool CanDelete { get; set; }
    public string DeleteBlockedReason { get; set; } = string.Empty;
}