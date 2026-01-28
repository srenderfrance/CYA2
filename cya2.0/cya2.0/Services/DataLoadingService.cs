using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using ModelsLibrary;

namespace cya2.Services
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

        // Initialize user data on first page load or when user identity changes
        public async Task InitializeUserDataAsync(ClaimsPrincipal user)
        {
            try
            {
                // Always set IsAdmin from current claims
                _appState.IsAdmin = user.IsInRole("Admin") ||
                                    user.FindFirstValue("AuthLevel")?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

                // Determine current user id from claims or DB
                int currentUserId = 0;
                var userIdClaim = user.FindFirstValue("UserId");
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int idFromClaim))
                {
                    currentUserId = idFromClaim;
                }
                else
                {
                    var email = user.FindFirstValue(ClaimTypes.Email);
                    if (!string.IsNullOrEmpty(email))
                    {
                        string userSql = "SELECT Id FROM Users WHERE Email = @Email";
                        var userResults = await _dataAccess.LoadData<UserIdOnly, dynamic>(userSql, new { Email = email }, _config.GetConnectionString("default"));
                        currentUserId = userResults?.FirstOrDefault()?.Id ?? 0;
                    }
                }

                // If user id changed between sessions, clear AppState so we don't keep previous user's data/flags
                bool userChanged = _appState.CurrentUserId != 0 && _appState.CurrentUserId != currentUserId;
                if (userChanged)
                {
                    _appState.ClearUserData();
                    _appState.UserAccountsLoaded = false;
                }

                // If already initialized for this user, nothing else to do
                if (_appState.UserAccountsLoaded && !userChanged)
                {
                    // Still update CurrentUserId to ensure consistency
                    _appState.CurrentUserId = currentUserId;
                    return;
                }

                // Store current user id
                _appState.CurrentUserId = currentUserId;

                // Load user accounts first
                await LoadUserAccountsAsync();

                // Get default account ID from claims
                var defaultAccountClaim = user.FindFirstValue("DefaultAccount");
                if (!string.IsNullOrEmpty(defaultAccountClaim) && int.TryParse(defaultAccountClaim, out int accountId))
                {
                    var defaultAccount = _appState.UserAccounts.FirstOrDefault(a => a.AccountId == accountId);
                    if (defaultAccount != null)
                    {
                        _appState.DefaultAccount = defaultAccount.Fund;
                        _appState.SelectedAccount = defaultAccount.Fund;

                        await LoadAccountDataAsync(defaultAccount);
                    }
                }
                else
                {
                    // No default account set; do not auto-load account data
                }

                // For non-admin users, start background loading of remaining accounts
                if (!_appState.IsAdmin && _appState.UserAccounts.Count > 1 && !string.IsNullOrEmpty(_appState.DefaultAccount))
                {
                    _ = Task.Run(async () => await LoadAllUserAccountDataAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing user data: {ex.Message}");
                throw;
            }
        }

        private async Task LoadUserAccountsAsync()
        {
            try
            {
                _appState.UserAccounts.Clear();

                string sql;
                object parameters;

                if (_appState.IsAdmin)
                {
                    sql = "SELECT AccountId, Fund, AccountingClass, CreatedAt, Overhead, AccountNumber, SoftCredit, BalanceAdjustment, OtherFunds FROM Accounts ORDER BY Fund";
                    parameters = new { };
                }
                else
                {
                    sql = @"
                        SELECT a.AccountId, a.Fund, a.AccountingClass, a.CreatedAt, a.Overhead, a.AccountNumber, a.SoftCredit, a.BalanceAdjustment, a.OtherFunds
                        FROM Accounts a
                        INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
                        WHERE au.UserId = @UserId
                        ORDER BY a.Fund";
                    parameters = new { UserId = _appState.CurrentUserId };
                }

                var accounts = await _dataAccess.LoadData<Account, dynamic>(sql, parameters, _config.GetConnectionString("default"));

                if (accounts?.Any() == true)
                {
                    _appState.UserAccounts = accounts.ToList();
                }

                _appState.UserAccountsLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading user accounts: {ex.Message}");
                _appState.UserAccountsLoaded = false;
            }
        }

        public async Task LoadAccountDataAsync(Account account)
        {
            try
            {
                if (_appState.IsAccountDataLoaded(account.Fund)) return;

                _appState.IsLoadingData = true;

                string accountingSql = @"
                    SELECT 
                        Id,
                        AccountingClass,
                        Date,
                        Num,
                        Amount,
                        AccountNumber,
                        Account,
                        Type,
                        DateCreated
                    FROM AccountingData
                    WHERE AccountingClass = @AccountClass";

                var accountingData = await _dataAccess.LoadData<AccountingDataModel, dynamic>(
                    accountingSql,
                    new { AccountClass = account.AccountingClass },
                    _config.GetConnectionString("default")
                );

                string donationSql = @"SELECT * FROM DonationData WHERE 
                                       Fund COLLATE utf8mb4_0900_ai_ci = @Fund COLLATE utf8mb4_0900_ai_ci
                                       OR Fund COLLATE utf8mb4_0900_ai_ci IN (
                                           SELECT SubFund COLLATE utf8mb4_0900_ai_ci 
                                           FROM SubAccounts 
                                           WHERE AccountId = @AccountId AND Kind = 'Merged'
                                       )";
                var donationData = await _dataAccess.LoadData<DonationsDataModel, dynamic>(
                    donationSql,
                    new { Fund = account.Fund, AccountId = account.AccountId },
                    _config.GetConnectionString("default")
                );

                _appState.SetAccountData(
                    account.Fund,
                    accountingData?.ToList() ?? new List<AccountingDataModel>(),
                    donationData?.ToList() ?? new List<DonationsDataModel>()
                );

                if (account.Fund == _appState.DefaultAccount)
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

        private async Task LoadAllUserAccountDataAsync()
        {
            try
            {
                _appState.IsLoadingData = true;

                foreach (var account in _appState.UserAccounts)
                {
                    if (account.Fund == _appState.DefaultAccount) continue;

                    if (!_appState.IsAccountDataLoaded(account.Fund))
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

        // Public helper to force refresh of user accounts from the database using the current principal
        public async Task ForceLoadUserAccountsAsync(ClaimsPrincipal user)
        {
            try
            {
                // Update admin flag from claims
                _appState.IsAdmin = user.IsInRole("Admin") ||
                                    user.FindFirstValue("AuthLevel")?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

                // Ensure CurrentUserId is set from claims if available
                var userIdClaim = user.FindFirstValue("UserId");
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int idFromClaim))
                {
                    _appState.CurrentUserId = idFromClaim;
                }

                await LoadUserAccountsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error forcing user accounts load: {ex.Message}");
                throw;
            }
        }

        private class UserIdOnly
        {
            public int Id { get; set; }
        }
    }
}