using ModelsLibrary;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace cya2._0.Services
{
    public class AppState
    {
        // Account data tracking
        public List<Account> UserAccounts { get; set; } = new List<Account>();
        public string DefaultAccount { get; set; } = string.Empty;
        public bool UserAccountsLoaded { get; set; } = false;
        public int CurrentUserId { get; set; }
        public bool IsAdmin { get; set; } = false;
        
        // Data tracking per account
        public Dictionary<string, AccountData> AccountDataCache { get; set; } = new Dictionary<string, AccountData>();
        
        // Background loading status
        public bool IsLoadingData { get; set; } = false;
        
        // Global data cache (for all accounts - primarily for admin users)
        public List<AccountingDataModel> AccountingData { get; set; } = new List<AccountingDataModel>();
        public List<DonationsDataModel> DonationData { get; set; } = new List<DonationsDataModel>();
        
        // Helper methods
        public bool IsAccountDataLoaded(string accountName) => 
            AccountDataCache.ContainsKey(accountName) && 
            AccountDataCache[accountName].IsLoaded;
            
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
            // Clear all user-specific data
            UserAccounts.Clear();
            DefaultAccount = string.Empty;
            UserAccountsLoaded = false;
            CurrentUserId = 0;
            IsAdmin = false;
            AccountDataCache.Clear();
            IsLoadingData = false;
            AccountingData.Clear();
            DonationData.Clear();
            
            // Log the reset
            Console.WriteLine("AppState has been cleared during logout");
        }
    }
    
    // Class to hold data for a specific account
    public class AccountData
    {
        public List<AccountingDataModel> AccountingData { get; set; } = new List<AccountingDataModel>();
        public List<DonationsDataModel> DonationData { get; set; } = new List<DonationsDataModel>();
        public bool IsLoaded { get; set; } = false;
    }
}