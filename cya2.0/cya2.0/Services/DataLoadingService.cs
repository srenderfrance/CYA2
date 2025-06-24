using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using ModelsLibrary;

namespace cya2._0.Services
{
    public class DataLoadingService
    {
        private readonly IDataAccess _dataAccess;
        private readonly IConfiguration _config;
        private readonly AppState _appState;

        public DataLoadingService(IDataAccess dataAccess, IConfiguration config, AppState appState)
        {
            _dataAccess = dataAccess;
            _config = config;
            _appState = appState;
        }

        // Initialize user data on first page load
        public async Task InitializeUserDataAsync(ClaimsPrincipal user)
        {
            try
            {
                // Skip if already initialized
                if (_appState.UserAccountsLoaded) return;

                // Check if user is Admin first
                _appState.IsAdmin = user.IsInRole("Admin") ||
                                    user.FindFirstValue("AuthLevel")?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

                Console.WriteLine($"User is Admin: {_appState.IsAdmin}");

                // Get user ID (needed for both admin and non-admin users)
                var userIdClaim = user.FindFirstValue("UserId");
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int id))
                {
                    _appState.CurrentUserId = id;
                    Console.WriteLine($"User ID from claims: {_appState.CurrentUserId}");
                }
                else
                {
                    // Fallback to database lookup
                    var email = user.FindFirstValue(ClaimTypes.Email);
                    Console.WriteLine($"User ID claim not found, using email: {email} for DB lookup");

                    if (!string.IsNullOrEmpty(email))
                    {
                        string userSql = "SELECT Id FROM Users WHERE Email = @Email";
                        var userResults = await _dataAccess.LoadData<UserIdOnly, dynamic>(
                            userSql,
                            new { Email = email },
                            _config.GetConnectionString("default")
                        );

                        if (userResults != null && userResults.Any())
                        {
                            _appState.CurrentUserId = userResults.First().Id;
                            Console.WriteLine($"User ID from database: {_appState.CurrentUserId}");
                        }
                        else
                        {
                            throw new Exception("Unable to identify the current user.");
                        }
                    }
                    else
                    {
                        throw new Exception("No email found in user claims.");
                    }
                }

                if (_appState.IsAdmin)
                {
                    // For admin users, just load the account list for the dropdown
                    await LoadUserAccountsAsync();

                    // Only load default account data if one is specified
                    var defaultAccountClaim = user.FindFirstValue("DefaultAccount");
                    if (!string.IsNullOrEmpty(defaultAccountClaim) && int.TryParse(defaultAccountClaim, out int accountId))
                    {
                        var defaultAccount = _appState.UserAccounts.FirstOrDefault(a => a.AccountId == accountId);
                        if (defaultAccount != null)
                        {
                            _appState.DefaultAccount = defaultAccount.Name;
                            Console.WriteLine($"Admin default account set to: {_appState.DefaultAccount}");
                            // Load data only for the default account
                            await LoadAccountDataAsync(defaultAccount);
                        }
                    }
                    // Don't set a default account or load any data if no default specified
                }
                else
                {
                    // Regular user behavior remains unchanged
                    await LoadUserAccountsAsync();

                    var defaultAccountClaim = user.FindFirstValue("DefaultAccount");
                    if (!string.IsNullOrEmpty(defaultAccountClaim) && int.TryParse(defaultAccountClaim, out int accountId))
                    {
                        var defaultAccount = _appState.UserAccounts.FirstOrDefault(a => a.AccountId == accountId);
                        if (defaultAccount != null)
                        {
                            _appState.DefaultAccount = defaultAccount.Name;
                            Console.WriteLine($"Default account set to: {_appState.DefaultAccount}");
                            await LoadAccountDataAsync(defaultAccount);
                        }
                    }
                    else if (_appState.UserAccounts.Any())
                    {
                        _appState.DefaultAccount = _appState.UserAccounts.First().Name;
                        Console.WriteLine($"No default account found, using first account: {_appState.DefaultAccount}");
                        await LoadAccountDataAsync(_appState.UserAccounts.First());
                    }

                    // Start background loading for non-admin users
                    if (_appState.UserAccounts.Count > 1)
                    {
                        _ = Task.Run(async () => await LoadAllUserAccountDataAsync());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing user data: {ex.Message}");
                throw; // Rethrow to allow calling code to handle
            }
        }

        // Load user accounts
        private async Task LoadUserAccountsAsync()
        {
            try
            {
                // Clear existing accounts list
                _appState.UserAccounts.Clear();

                string sql;
                object parameters;

                if (_appState.IsAdmin)
                {
                    // Admin can see all accounts
                    sql = "SELECT AccountId, Name, AccountRef, CreatedAt FROM Accounts ORDER BY Name";
                    parameters = new { };

                    Console.WriteLine($"Loading all accounts for admin user");
                }
                else
                {
                    // Regular users only see their linked accounts from AccountsUsers table
                    sql = @"
                        SELECT a.AccountId, a.Name, a.AccountRef, a.CreatedAt
                        FROM Accounts a
                        INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
                        WHERE au.UserId = @UserId
                        ORDER BY a.Name";
                    parameters = new { UserId = _appState.CurrentUserId };

                    Console.WriteLine($"Loading accounts for user ID: {_appState.CurrentUserId}");
                }

                var accounts = await _dataAccess.LoadData<Account, dynamic>(
                    sql,
                    parameters,
                    _config.GetConnectionString("default")
                );

                if (accounts?.Any() == true)
                {
                    _appState.UserAccounts = accounts.ToList();
                    Console.WriteLine($"Loaded {_appState.UserAccounts.Count} accounts for user");
                }
                else
                {
                    Console.WriteLine("No accounts found for user");
                }

                _appState.UserAccountsLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading user accounts: {ex.Message}");
                _appState.UserAccountsLoaded = false;
            }
        }

        // Load data for a specific account
        public async Task LoadAccountDataAsync(Account account)
        {
            try
            {
                // Skip if already loaded
                if (_appState.IsAccountDataLoaded(account.Name)) return;

                _appState.IsLoadingData = true;

                // Extract designation from account reference
                string designation;
                if (account.AccountRef.Contains(':'))
                {
                    int colonIndex = account.AccountRef.IndexOf(':');
                    designation = account.AccountRef.Substring(colonIndex + 1).Trim();
                }
                else
                {
                    designation = account.AccountRef.Trim();
                }

                // Load accounting data
                string accountingSql = "SELECT * FROM quickbooks WHERE designation LIKE @Designation";
                var accountingData = await _dataAccess.LoadData<AccountingDataModel, dynamic>(
                    accountingSql,
                    new { Designation = $"%{designation}%" },
                    _config.GetConnectionString("default")
                );

                // Load donation data
                string donationSql = "SELECT * FROM etapestry WHERE fund = @Fund";
                var donationData = await _dataAccess.LoadData<DonationsDataModel, dynamic>(
                    donationSql,
                    new { Fund = account.AccountRef },
                    _config.GetConnectionString("default")
                );

                // Store in AppState
                _appState.SetAccountData(
                    account.Name,
                    accountingData?.ToList() ?? new List<AccountingDataModel>(),
                    donationData?.ToList() ?? new List<DonationsDataModel>()
                );

                // For backward compatibility with existing code
                if (account.Name == _appState.DefaultAccount)
                {
                    _appState.AccountingData = accountingData?.ToList() ?? new List<AccountingDataModel>();
                }

                _appState.IsLoadingData = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading account data: {ex.Message}");
                _appState.IsLoadingData = false;
            }
        }

        // Background load all user account data (for non-admin users)
        private async Task LoadAllUserAccountDataAsync()
        {
            try
            {
                _appState.IsLoadingData = true;

                foreach (var account in _appState.UserAccounts)
                {
                    // Skip the default account as it's already loaded
                    if (account.Name == _appState.DefaultAccount) continue;

                    // Check if data is already loaded
                    if (!_appState.IsAccountDataLoaded(account.Name))
                    {
                        await LoadAccountDataAsync(account);
                    }
                }

                _appState.IsLoadingData = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in background loading: {ex.Message}");
                _appState.IsLoadingData = false;
            }
        }

        // Helper class for user ID lookup
        private class UserIdOnly
        {
            public int Id { get; set; }
        }
    }
}