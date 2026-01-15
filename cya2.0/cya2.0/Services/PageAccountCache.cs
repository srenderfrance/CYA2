using ModelsLibrary;
using DataLibrary;
using Microsoft.Extensions.Configuration;

namespace cya2.Services
{
    /// <summary>
    /// Handles page-level account data caching for fast switching between accounts on the same page
    /// </summary>
    public class PageAccountCache
    {
        private readonly Dictionary<string, PageAccountData> _pageCache = new();
        private readonly AppState _appState;
        private readonly IDataAccess _dataAccess;
        private readonly IConfiguration _config;
        private readonly DataLoadingService _dataLoader;

        public PageAccountCache(AppState appState, IDataAccess dataAccess, IConfiguration config, DataLoadingService dataLoader)
        {
            _appState = appState;
            _dataAccess = dataAccess;
            _config = config;
            _dataLoader = dataLoader;
        }

        /// <summary>
        /// Gets account data for the specified account, using page cache, AppState cache, or database
        /// </summary>
        public async Task<PageAccountData?> GetAccountDataAsync(string accountFund)
        {
            if (string.IsNullOrEmpty(accountFund)) return null;

            // Check page cache first
            if (_pageCache.ContainsKey(accountFund))
            {
                return _pageCache[accountFund];
            }

            // Check AppState cache
            if (_appState.IsAccountDataLoaded(accountFund))
            {
                var appStateData = _appState.AccountDataCache[accountFund];
                var pageData = new PageAccountData
                {
                    AccountingData = appStateData.AccountingData,
                    DonationData = appStateData.DonationData
                };
                
                // Store in page cache
                _pageCache[accountFund] = pageData;
                return pageData;
            }

            // Need to load from database
            var account = _appState.UserAccounts.FirstOrDefault(a => a.Fund == accountFund);
            if (account == null) return null;

            await _dataLoader.LoadAccountDataAsync(account);
            
            if (_appState.IsAccountDataLoaded(accountFund))
            {
                var appStateData = _appState.AccountDataCache[accountFund];
                var pageData = new PageAccountData
                {
                    AccountingData = appStateData.AccountingData,
                    DonationData = appStateData.DonationData
                };
                
                // Store in page cache
                _pageCache[accountFund] = pageData;
                
                // Update AppState selected account management
                _appState.SetSelectedAccount(accountFund);
                
                return pageData;
            }

            return null;
        }

        /// <summary>
        /// Clears the page-level cache (call when navigating away from page)
        /// </summary>
        public void ClearPageCache()
        {
            _pageCache.Clear();
        }

        /// <summary>
        /// Checks if account data is available in page cache
        /// </summary>
        public bool IsAccountCached(string accountFund)
        {
            return _pageCache.ContainsKey(accountFund);
        }
    }

    public class PageAccountData
    {
        public List<AccountingDataModel> AccountingData { get; set; } = new();
        public List<DonationsDataModel> DonationData { get; set; } = new();
    }
}