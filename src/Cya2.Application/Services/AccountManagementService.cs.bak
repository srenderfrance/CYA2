using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using DataLibrary;
using Microsoft.Extensions.Configuration;

namespace Cya2.Application.Services;

/// <summary>
/// Service for account and sub-account management business logic
/// Moves complex validation and CRUD logic from Admin.razor component
/// </summary>
public class AccountManagementService : IAccountManagementService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _config;

    public AccountManagementService(IDataAccess dataAccess, IConfiguration config)
    {
        _dataAccess = dataAccess;
        _config = config;
    }

    /// <summary>
    /// Create new primary fundraising account - moved from Admin.razor AddNewFund()
    /// </summary>
    public async Task<AccountOperationResult> CreateAccountAsync(CreateAccountRequest request)
    {
        var result = new AccountOperationResult();
        
        try
        {
            // Validate input data
            var validation = await ValidateAccountDataAsync(new AccountValidationRequest
            {
                Fund = request.Fund,
                AccountNumber = request.AccountNumber,
                AccountingClass = request.AccountingClass
            });

            if (!validation.IsValid)
            {
                result.Errors = validation.Errors;
                result.Message = "Validation failed";
                return result;
            }

            // Check for duplicates
            var duplicateCheck = await CheckForDuplicateAccountAsync(request.Fund, request.AccountNumber);
            if (!duplicateCheck.IsValid)
            {
                result.Errors = duplicateCheck.Errors;
                result.Message = "Duplicate account detected";
                return result;
            }

            // Create account using entity
            var account = new Account(
                request.Fund.Trim(),
                request.AccountingClass.Trim(),
                request.AccountNumber.Trim(),
                request.Overhead
            );

            account.UpdateSoftCredit(request.SoftCredit);
            account.UpdateBalanceAdjustment(request.BalanceAdjustment);

            // Save to database
            const string sql = @"
                INSERT INTO Accounts (Fund, AccountingClass, AccountNumber, SoftCredit, BalanceAdjustment, CreatedAt, Overhead)
                VALUES (@Fund, @AccountingClass, @AccountNumber, @SoftCredit, @BalanceAdjustment, @CreatedAt, @Overhead)";

            var insertResult = await _dataAccess.SaveData(
                sql,
                new { 
                    Fund = account.Fund,
                    AccountingClass = account.AccountingClass,
                    AccountNumber = account.AccountNumber,
                    SoftCredit = account.SoftCredit,
                    BalanceAdjustment = account.BalanceAdjustment,
                    CreatedAt = DateTime.UtcNow,
                    Overhead = account.Overhead
                },
                GetConnectionString()
            );

            if (insertResult > 0)
            {
                result.IsSuccess = true;
                result.Message = $"Account '{request.Fund}' created successfully";
                
                // Load the created account with its ID
                result.CreatedAccount = await GetAccountByFundAsync(request.Fund);
            }
            else
            {
                result.Message = "Failed to create account in database";
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Error creating account: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Update existing account - moved from Admin.razor UpdateFund()
    /// </summary>
    public async Task<AccountOperationResult> UpdateAccountAsync(UpdateAccountRequest request)
    {
        var result = new AccountOperationResult();
        
        try
        {
            // Validate input data
            var validation = await ValidateAccountDataAsync(new AccountValidationRequest
            {
                Fund = request.Fund,
                AccountNumber = request.AccountNumber,
                AccountingClass = request.AccountingClass,
                ExistingAccountId = request.AccountId
            });

            if (!validation.IsValid)
            {
                result.Errors = validation.Errors;
                result.Message = "Validation failed";
                return result;
            }

            // Check for duplicates (excluding current account)
            var duplicateCheck = await CheckForDuplicateAccountAsync(request.Fund, request.AccountNumber, request.AccountId);
            if (!duplicateCheck.IsValid)
            {
                result.Errors = duplicateCheck.Errors;
                result.Message = "Duplicate account detected";
                return result;
            }

            // Update in database
            const string sql = @"
                UPDATE Accounts 
                SET Fund = @Fund, AccountingClass = @AccountingClass, AccountNumber = @AccountNumber, 
                    SoftCredit = @SoftCredit, BalanceAdjustment = @BalanceAdjustment, Overhead = @Overhead
                WHERE AccountId = @AccountId";

            var updateResult = await _dataAccess.SaveData(
                sql,
                new { 
                    Fund = request.Fund.Trim(),
                    AccountingClass = request.AccountingClass.Trim(),
                    AccountNumber = request.AccountNumber.Trim(),
                    SoftCredit = request.SoftCredit?.Trim() ?? string.Empty,
                    BalanceAdjustment = request.BalanceAdjustment,
                    Overhead = request.Overhead,
                    AccountId = request.AccountId
                },
                GetConnectionString()
            );

            if (updateResult > 0)
            {
                result.IsSuccess = true;
                result.Message = "Account updated successfully";
            }
            else
            {
                result.Message = "Failed to update account in database";
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Error updating account: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Delete account with dependency checking - moved from Admin.razor
    /// </summary>
    public async Task<AccountOperationResult> DeleteAccountAsync(int accountId)
    {
        var result = new AccountOperationResult();
        
        try
        {
            // Check dependencies first
            var dependencies = await CheckAccountDependenciesAsync(accountId);
            if (!dependencies.CanDelete)
            {
                result.Message = dependencies.DeleteBlockedReason;
                result.Errors.Add(dependencies.DeleteBlockedReason);
                return result;
            }

            // Delete the account
            const string sql = "DELETE FROM Accounts WHERE AccountId = @AccountId";
            var deleteResult = await _dataAccess.SaveData(sql, new { AccountId = accountId }, GetConnectionString());

            if (deleteResult > 0)
            {
                result.IsSuccess = true;
                result.Message = "Account deleted successfully";
            }
            else
            {
                result.Message = "Failed to delete account";
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Error deleting account: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Create sub-account - moved from Admin.razor AddNewFund() subfund logic
    /// </summary>
    public async Task<SubAccountOperationResult> CreateSubAccountAsync(CreateSubAccountRequest request)
    {
        var result = new SubAccountOperationResult();
        
        try
        {
            // Validate input
            if (request.PrimaryAccountId <= 0)
            {
                result.Errors.Add("Primary account ID is required");
                result.Message = "Validation failed";
                return result;
            }

            if (string.IsNullOrWhiteSpace(request.SubFund))
            {
                result.Errors.Add("Sub fund name is required");
                result.Message = "Validation failed";
                return result;
            }

            if (request.SubFund.Length < 2 || request.SubFund.Length > 255)
            {
                result.Errors.Add("Sub fund name must be between 2 and 255 characters");
                result.Message = "Validation failed";
                return result;
            }

            // Check for duplicates within same primary account
            const string dupSql = @"SELECT COUNT(*) FROM SubAccounts WHERE AccountId = @AccountId AND SubFund = @SubFund";
            var dupCountRes = await _dataAccess.LoadData<int, dynamic>(dupSql, 
                new { AccountId = request.PrimaryAccountId, SubFund = request.SubFund.Trim() }, 
                GetConnectionString());
            
            int dupCount = dupCountRes?.FirstOrDefault() ?? 0;
            if (dupCount > 0)
            {
                result.Errors.Add($"A sub fund named '{request.SubFund.Trim()}' already exists for the selected account");
                result.Message = "Duplicate sub fund detected";
                return result;
            }

            // Create sub-account using entity
            var subAccount = new SubAccount
            {
                AccountId = request.PrimaryAccountId,
                SubFund = request.SubFund.Trim(),
                Kind = request.Kind.Trim()
            };

            // Insert into database
            const string insertSql = @"INSERT INTO SubAccounts (AccountId, SubFund, Kind) VALUES (@AccountId, @SubFund, @Kind)";
            var insertResult = await _dataAccess.SaveData(insertSql, 
                new { 
                    AccountId = request.PrimaryAccountId, 
                    SubFund = request.SubFund.Trim(), 
                    Kind = request.Kind.Trim() 
                }, 
                GetConnectionString());

            if (insertResult > 0)
            {
                result.IsSuccess = true;
                result.Message = $"Sub fund '{request.SubFund.Trim()}' created successfully";
                result.CreatedSubAccount = subAccount;
            }
            else
            {
                result.Message = "Failed to create sub fund";
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Error creating sub fund: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Update sub-account - moved from Admin.razor UpdateSubAccountAsync()
    /// </summary>
    public async Task<SubAccountOperationResult> UpdateSubAccountAsync(UpdateSubAccountRequest request)
    {
        var result = new SubAccountOperationResult();
        
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.SubFund))
            {
                result.Errors.Add("Sub fund name is required");
                result.Message = "Validation failed";
                return result;
            }

            // Check for duplicates (excluding current sub-account)
            const string dupSql = @"SELECT COUNT(*) FROM SubAccounts WHERE AccountId = @AccountId AND SubFund = @SubFund AND Id <> @Id";
            var dupCountRes = await _dataAccess.LoadData<int, dynamic>(dupSql, 
                new { 
                    AccountId = request.PrimaryAccountId, 
                    SubFund = request.SubFund.Trim(), 
                    Id = request.SubAccountId 
                }, 
                GetConnectionString());
            
            int dupCount = dupCountRes?.FirstOrDefault() ?? 0;
            if (dupCount > 0)
            {
                result.Errors.Add($"A sub fund named '{request.SubFund.Trim()}' already exists for this account");
                result.Message = "Duplicate sub fund detected";
                return result;
            }

            // Update in database
            const string updateSql = @"UPDATE SubAccounts SET SubFund = @SubFund, Kind = @Kind WHERE Id = @Id";
            var updateResult = await _dataAccess.SaveData(updateSql, 
                new { 
                    SubFund = request.SubFund.Trim(), 
                    Kind = request.Kind.Trim(), 
                    Id = request.SubAccountId 
                }, 
                GetConnectionString());

            if (updateResult > 0)
            {
                result.IsSuccess = true;
                result.Message = "Sub fund updated successfully";
            }
            else
            {
                result.Message = "Failed to update sub fund";
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Error updating sub fund: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Delete sub-account - moved from Admin.razor DeleteSubAccountAsync()
    /// </summary>
    public async Task<SubAccountOperationResult> DeleteSubAccountAsync(int subAccountId)
    {
        var result = new SubAccountOperationResult();
        
        try
        {
            const string deleteSql = "DELETE FROM SubAccounts WHERE Id = @Id";
            var deleteResult = await _dataAccess.SaveData(deleteSql, new { Id = subAccountId }, GetConnectionString());

            if (deleteResult > 0)
            {
                result.IsSuccess = true;
                result.Message = "Sub fund deleted successfully";
            }
            else
            {
                result.Message = "Failed to delete sub fund";
            }
        }
        catch (Exception ex)
        {
            result.Message = $"Error deleting sub fund: {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    /// <summary>
    /// Get all accounts for dropdowns
    /// </summary>
    public async Task<List<Account>> GetAllAccountsAsync()
    {
        try
        {
            const string sql = @"SELECT AccountId, Fund, AccountingClass, AccountNumber, CreatedAt, Overhead, 
                                SoftCredit, BalanceAdjustment 
                                FROM Accounts ORDER BY Fund";
            
            var accounts = await _dataAccess.LoadData<Account, dynamic>(sql, new { }, GetConnectionString());
            return accounts?.ToList() ?? new List<Account>();
        }
        catch
        {
            return new List<Account>();
        }
    }

    /// <summary>
    /// Get sub-accounts for primary account
    /// </summary>
    public async Task<List<SubAccount>> GetSubAccountsAsync(int primaryAccountId)
    {
        try
        {
            const string sql = "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId";
            var subAccounts = await _dataAccess.LoadData<SubAccount, dynamic>(sql, new { AccountId = primaryAccountId }, GetConnectionString());
            return subAccounts?.ToList() ?? new List<SubAccount>();
        }
        catch
        {
            return new List<SubAccount>();
        }
    }

    /// <summary>
    /// Validate account data
    /// </summary>
    public async Task<ValidationResult> ValidateAccountDataAsync(AccountValidationRequest request)
    {
        var result = new ValidationResult { IsValid = true };

        // Required field validation
        if (string.IsNullOrWhiteSpace(request.Fund))
        {
            result.Errors.Add("Fund name is required");
            result.IsValid = false;
        }
        else if (request.Fund.Length < 2 || request.Fund.Length > 100)
        {
            result.Errors.Add("Fund name must be between 2 and 100 characters");
            result.IsValid = false;
        }

        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            result.Errors.Add("Account number is required");
            result.IsValid = false;
        }

        if (string.IsNullOrWhiteSpace(request.AccountingClass))
        {
            result.Errors.Add("Accounting class is required");
            result.IsValid = false;
        }

        return result;
    }

    /// <summary>
    /// Check account dependencies before deletion
    /// </summary>
    public async Task<AccountDependencyResult> CheckAccountDependenciesAsync(int accountId)
    {
        var result = new AccountDependencyResult { CanDelete = true };

        try
        {
            // Check for donations
            const string donationsSql = @"SELECT COUNT(*) FROM DonationData d 
                                         INNER JOIN Accounts a ON d.Fund = a.Fund 
                                         WHERE a.AccountId = @AccountId";
            var donationCount = await _dataAccess.LoadData<int, dynamic>(donationsSql, new { AccountId = accountId }, GetConnectionString());
            int donations = donationCount?.FirstOrDefault() ?? 0;

            // Check for user assignments
            const string usersSql = "SELECT COUNT(*) FROM AccountsUsers WHERE AccountId = @AccountId";
            var userCount = await _dataAccess.LoadData<int, dynamic>(usersSql, new { AccountId = accountId }, GetConnectionString());
            int users = userCount?.FirstOrDefault() ?? 0;

            // Check for sub-accounts
            const string subAccountsSql = "SELECT COUNT(*) FROM SubAccounts WHERE AccountId = @AccountId";
            var subAccountCount = await _dataAccess.LoadData<int, dynamic>(subAccountsSql, new { AccountId = accountId }, GetConnectionString());
            int subAccounts = subAccountCount?.FirstOrDefault() ?? 0;

            // Build dependency list
            if (donations > 0)
            {
                result.Dependencies.Add($"{donations} donation(s)");
                result.HasDependencies = true;
            }

            if (users > 0)
            {
                result.Dependencies.Add($"{users} user assignment(s)");
                result.HasDependencies = true;
            }

            if (subAccounts > 0)
            {
                result.Dependencies.Add($"{subAccounts} sub-account(s)");
                result.HasDependencies = true;
            }

            // Determine if deletion is allowed
            if (result.HasDependencies)
            {
                result.CanDelete = false;
                result.DeleteBlockedReason = $"Cannot delete account with dependencies: {string.Join(", ", result.Dependencies)}";
            }
        }
        catch (Exception ex)
        {
            result.CanDelete = false;
            result.DeleteBlockedReason = $"Error checking dependencies: {ex.Message}";
        }

        return result;
    }

    // Helper methods
    private async Task<ValidationResult> CheckForDuplicateAccountAsync(string fund, string accountNumber, int? excludeAccountId = null)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            // Check fund name
            string fundSql = "SELECT COUNT(*) FROM Accounts WHERE Fund = @Fund";
            var fundParams = new { Fund = fund };

            if (excludeAccountId.HasValue)
            {
                fundSql += " AND AccountId <> @ExcludeId";
                fundParams = new { Fund = fund, ExcludeId = excludeAccountId.Value };
            }

            var fundCount = await _dataAccess.LoadData<int, dynamic>(fundSql, fundParams, GetConnectionString());
            if (fundCount?.FirstOrDefault() > 0)
            {
                result.Errors.Add($"A fund with the name '{fund}' already exists");
                result.IsValid = false;
            }

            // Check account number
            string numberSql = "SELECT COUNT(*) FROM Accounts WHERE AccountNumber = @AccountNumber";
            var numberParams = new { AccountNumber = accountNumber };

            if (excludeAccountId.HasValue)
            {
                numberSql += " AND AccountId <> @ExcludeId";
                numberParams = new { AccountNumber = accountNumber, ExcludeId = excludeAccountId.Value };
            }

            var numberCount = await _dataAccess.LoadData<int, dynamic>(numberSql, numberParams, GetConnectionString());
            if (numberCount?.FirstOrDefault() > 0)
            {
                result.Errors.Add($"A fund with the number '{accountNumber}' already exists");
                result.IsValid = false;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error checking for duplicates: {ex.Message}");
            result.IsValid = false;
        }

        return result;
    }

    private async Task<Account?> GetAccountByFundAsync(string fund)
    {
        try
        {
            const string sql = "SELECT * FROM Accounts WHERE Fund = @Fund";
            var accounts = await _dataAccess.LoadData<Account, dynamic>(sql, new { Fund = fund }, GetConnectionString());
            return accounts?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private string GetConnectionString()
    {
        return _config.GetConnectionString("default") ?? string.Empty;
    }
}