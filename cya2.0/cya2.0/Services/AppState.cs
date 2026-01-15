using ModelsLibrary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using UserAuth;

namespace cya2.Services
{
    public class AppState
    {
        // Account data tracking
        public List<Account> UserAccounts { get; set; } = new List<Account>();
        public string DefaultAccount { get; set; } = string.Empty;
        public string SelectedAccount { get; set; } = string.Empty; // Current selected account for cross-page navigation
        public bool UserAccountsLoaded { get; set; } = false;
        public int CurrentUserId { get; set; } = 0;
        
        // Auth level properties
        public bool IsAdmin { get; set; } = false;
        public bool IsViewer { get; set; } = false;
        public string AuthLevel { get; set; } = string.Empty;
        
        // Data tracking per account - now with selected account support
        public Dictionary<string, AccountData> AccountDataCache { get; set; } = new Dictionary<string, AccountData>();
        
        // Cross-page state persistence
        public DateTime? SelectedStartDate { get; set; }
        public DateTime? SelectedEndDate { get; set; }
        public string SelectedDatePreset { get; set; } = "ThisYear";
        
        // Background loading status
        public bool IsLoadingData { get; set; } = false;
        
        // Global data cache (for all accounts - primarily for admin users)
        public List<AccountingDataModel> AccountingData { get; set; } = new List<AccountingDataModel>();
        public List<DonationsDataModel> DonationData { get; set; } = new List<DonationsDataModel>();
        
        // Initialize user data from claims
        public void InitializeFromClaims(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated == true)
            {
                var authLevel = user.FindFirstValue("AuthLevel") ?? "User";
                AuthLevel = authLevel;
                
                // Set role-based properties
                IsAdmin = authLevel == UserAuth.AuthLevel.Admin.ToString();
                IsViewer = authLevel == UserAuth.AuthLevel.Viewer.ToString();
                
                // Get user ID
                if (int.TryParse(user.FindFirstValue("UserId"), out int userId))
                {
                    CurrentUserId = userId;
                }
                
                // Get default account (stored as AccountId)
                if (int.TryParse(user.FindFirstValue("DefaultAccount"), out int defaultAccountId))
                {
                    // Will be resolved to Fund name after accounts are loaded
                }
            }
        }
        
        // Helper methods
        public bool IsAccountDataLoaded(string accountName)
        {
            return AccountDataCache.ContainsKey(accountName) && 
                   AccountDataCache[accountName].IsLoaded;
        }
        
        public void SetAccountData(string accountName, List<AccountingDataModel> accountingData, List<DonationsDataModel> donationData)
        {
            if (!AccountDataCache.ContainsKey(accountName))
            {
                AccountDataCache[accountName] = new AccountData();
            }
            
            AccountDataCache[accountName].AccountingData = accountingData;
            AccountDataCache[accountName].DonationData = donationData;
            AccountDataCache[accountName].IsLoaded = true;
        }
        
        // New method to set the selected account and manage cache
        public void SetSelectedAccount(string accountName)
        {
            // If selecting a different account than default, manage the cache
            if (!string.IsNullOrEmpty(accountName) && accountName != DefaultAccount)
            {
                // Remove any existing non-default account data (except the new selection)
                var keysToRemove = AccountDataCache.Keys
                    .Where(key => key != DefaultAccount && key != accountName)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    AccountDataCache.Remove(key);
                }
                
                SelectedAccount = accountName;
            }
            else if (!string.IsNullOrEmpty(accountName) && accountName == DefaultAccount)
            {
                // Selecting default account, clear any non-default data
                var keysToRemove = AccountDataCache.Keys
                    .Where(key => key != DefaultAccount)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    AccountDataCache.Remove(key);
                }
                
                SelectedAccount = accountName;
            }
        }
        
        public void ClearUserData()
        {
            UserAccounts.Clear();
            UserAccountsLoaded = false;
            AccountDataCache.Clear();
            AccountingData.Clear();
            DonationData.Clear();
            IsAdmin = false;
            IsViewer = false;
            AuthLevel = string.Empty;
            CurrentUserId = 0;
            DefaultAccount = string.Empty;
            SelectedAccount = string.Empty;
            SelectedStartDate = null;
            SelectedEndDate = null;
            SelectedDatePreset = "ThisYear";
        }
        
        // Property to check if user can view all accounts (both Admin and Viewer)
        public bool CanViewAllAccounts => IsAdmin || IsViewer;
    }
    
    // Helper class to store data for an account
    public class AccountData
    {
        public List<AccountingDataModel> AccountingData { get; set; } = new List<AccountingDataModel>();
        public List<DonationsDataModel> DonationData { get; set; } = new List<DonationsDataModel>();
        public bool IsLoaded { get; set; } = false;
    }
}