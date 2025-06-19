using ModelsLibrary;
using System.Collections.Generic;
using System.Security.Claims;
using UserAuth;

namespace cya2._0.Services
{
    public class AppState
    {
        // Account data tracking
        public List<Account> UserAccounts { get; set; } = new List<Account>();
        public string DefaultAccount { get; set; } = string.Empty;
        public bool UserAccountsLoaded { get; set; } = false;
        public int CurrentUserId { get; set; } = 0;
        
        // Auth level properties
        public bool IsAdmin { get; set; } = false;
        public bool IsViewer { get; set; } = false;
        public string AuthLevel { get; set; } = string.Empty;
        
        // Data tracking per account
        public Dictionary<string, AccountData> AccountDataCache { get; set; } = new Dictionary<string, AccountData>();
        
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
                
                // Get default account
                DefaultAccount = user.FindFirstValue("DefaultAccount") ?? string.Empty;
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