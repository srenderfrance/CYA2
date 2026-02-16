using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using ModelsLibrary;

namespace UtilityClasses
{
    /// <summary>
    /// Helper class for managing SubAccount (secondary fund) operations,
    /// particularly for handling "Separate" sub accounts in donation displays.
    /// </summary>
    public class SubAccountHelper
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;

        public SubAccountHelper(IDataAccess data, IConfiguration config)
        {
            _data = data;
            _config = config;
        }

        /// <summary>
        /// Load all SubAccounts for a specific primary account
        /// </summary>
        public async Task<List<SubAccount>> LoadSubAccountsAsync(int accountId)
        {
            try
            {
                const string sql = "SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId";
                var conn = _config.GetConnectionString("default") ?? string.Empty;
                
                var data = await _data.LoadData<SubAccount, dynamic>(sql, new { AccountId = accountId }, conn);
                return data?.ToList() ?? new List<SubAccount>();
            }
            catch
            {
                return new List<SubAccount>();
            }
        }

        /// <summary>
        /// Check if an account has any "Separate" type sub accounts
        /// </summary>
        public async Task<bool> HasSeparateSubAccountsAsync(int accountId)
        {
            var subAccounts = await LoadSubAccountsAsync(accountId);
            return subAccounts.Any(sa => string.Equals(sa.Kind, "Separate", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all "Separate" sub accounts for a primary account
        /// </summary>
        public async Task<List<SubAccount>> GetSeparateSubAccountsAsync(int accountId)
        {
            var subAccounts = await LoadSubAccountsAsync(accountId);
            return subAccounts.Where(sa => string.Equals(sa.Kind, "Separate", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// Build dropdown options for sub account selection
        /// Includes "All", primary account name as "Default", and individual separate sub accounts
        /// </summary>
        public async Task<List<SubAccountOption>> GetSubAccountOptionsAsync(Account primaryAccount)
        {
            if (primaryAccount == null) return new List<SubAccountOption>();

            var options = new List<SubAccountOption>();
            
            // Always add "All" option first
            options.Add(new SubAccountOption
            {
                Value = "All",
                DisplayText = "All",
                IsAll = true,
                IsPrimary = false,
                SubAccountId = null,
                SubFund = null
            });

            // Add primary account as "Default" 
            options.Add(new SubAccountOption
            {
                Value = "Primary",
                DisplayText = $"{FundDisplayHelper.GetDisplay(primaryAccount.Fund)}",
                IsAll = false,
                IsPrimary = true,
                SubAccountId = null,
                SubFund = primaryAccount.Fund
            });

            // Add separate sub accounts
            var separateSubAccounts = await GetSeparateSubAccountsAsync(primaryAccount.AccountId);
            foreach (var subAccount in separateSubAccounts)
            {
                options.Add(new SubAccountOption
                {
                    Value = $"Sub_{subAccount.Id}",
                    DisplayText = FundDisplayHelper.GetDisplay(subAccount.SubFund),
                    IsAll = false,
                    IsPrimary = false,
                    SubAccountId = subAccount.Id,
                    SubFund = subAccount.SubFund
                });
            }

            return options;
        }

        /// <summary>
        /// Get fund names that should be included for a given sub account selection
        /// </summary>
        public async Task<List<string>> GetFundNamesForSelectionAsync(Account primaryAccount, string selectedValue)
        {
            if (primaryAccount == null) return new List<string>();

            var fundNames = new List<string>();

            if (selectedValue == "All")
            {
                // Include primary fund
                fundNames.Add(primaryAccount.Fund);
                
                // Include all separate sub account fund names
                var separateSubAccounts = await GetSeparateSubAccountsAsync(primaryAccount.AccountId);
                fundNames.AddRange(separateSubAccounts.Select(sa => sa.SubFund));
            }
            else if (selectedValue == "Primary")
            {
                // Only primary fund
                fundNames.Add(primaryAccount.Fund);
            }
            else if (selectedValue.StartsWith("Sub_"))
            {
                // Specific sub account - extract ID and get its fund name
                if (int.TryParse(selectedValue.Substring(4), out int subAccountId))
                {
                    var subAccounts = await LoadSubAccountsAsync(primaryAccount.AccountId);
                    var targetSubAccount = subAccounts.FirstOrDefault(sa => sa.Id == subAccountId && 
                        string.Equals(sa.Kind, "Separate", StringComparison.OrdinalIgnoreCase));
                    
                    if (targetSubAccount != null)
                    {
                        fundNames.Add(targetSubAccount.SubFund);
                    }
                }
            }

            return fundNames;
        }

        /// <summary>
        /// Get display name for the currently selected sub account option
        /// </summary>
        public async Task<string> GetSelectedDisplayNameAsync(Account primaryAccount, string selectedValue)
        {
            if (primaryAccount == null) return string.Empty;

            if (selectedValue == "All")
            {
                return "All";
            }
            else if (selectedValue == "Primary")
            {
                return FundDisplayHelper.GetDisplay(primaryAccount.Fund);
            }
            else if (selectedValue.StartsWith("Sub_"))
            {
                if (int.TryParse(selectedValue.Substring(4), out int subAccountId))
                {
                    var subAccounts = await LoadSubAccountsAsync(primaryAccount.AccountId);
                    var targetSubAccount = subAccounts.FirstOrDefault(sa => sa.Id == subAccountId);
                    if (targetSubAccount != null)
                    {
                        return FundDisplayHelper.GetDisplay(targetSubAccount.SubFund);
                    }
                }
            }

            return FundDisplayHelper.GetDisplay(primaryAccount.Fund); // fallback
        }
    }

    /// <summary>
    /// Represents an option in the sub account dropdown
    /// </summary>
    public class SubAccountOption
    {
        public string Value { get; set; } = string.Empty;
        public string DisplayText { get; set; } = string.Empty;
        public bool IsAll { get; set; }
        public bool IsPrimary { get; set; }
        public int? SubAccountId { get; set; }
        public string? SubFund { get; set; }
    }
}