using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class FinancialDashboardService : IFinancialDashboardService
{
    private readonly ILogger<FinancialDashboardService> _logger;
    private readonly IAccountCalculationService _accountCalculationService;
    private readonly IReverseBalanceCalculationService _reverseBalanceCalculationService;
    private readonly IDonationDataCache _donationDataCache;
    private readonly IDonationDataCacheLoader _donationDataCacheLoader;
    private readonly IAccountingDataCache _accountingDataCache;
    private readonly IAccountingDataCacheLoader _accountingDataCacheLoader;
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IDonationService _donationService;
    private readonly IUserAuthorizationService _authorizationService;
    private readonly IAccountBalanceCache _accountBalanceCache;
    private readonly IHomeSummaryCache _homeSummaryCache;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly IAccountFactDataService _accountFactDataService;

    public FinancialDashboardService(
        ILogger<FinancialDashboardService> logger,
        IAccountCalculationService accountCalculationService,
        IReverseBalanceCalculationService reverseBalanceCalculationService,
        IDonationDataCache donationDataCache,
        IDonationDataCacheLoader donationDataCacheLoader,
        IAccountingDataCache accountingDataCache,
        IAccountingDataCacheLoader accountingDataCacheLoader,
        IUserAccountContextService userAccountContextService,
        IDonationService donationService,
        IUserAuthorizationService authorizationService,
        IAccountBalanceCache accountBalanceCache,
        ICacheInvalidationVersion cacheInvalidationVersion,
        IHomeSummaryCache homeSummaryCache,
        IAccountFactDataService accountFactDataService)
    {
        _logger = logger;
        _accountCalculationService = accountCalculationService;
        _reverseBalanceCalculationService = reverseBalanceCalculationService;
        _donationDataCache = donationDataCache;
        _donationDataCacheLoader = donationDataCacheLoader;
        _accountingDataCache = accountingDataCache;
        _accountingDataCacheLoader = accountingDataCacheLoader;
        _userAccountContextService = userAccountContextService;
        _donationService = donationService;
        _authorizationService = authorizationService;
        _accountBalanceCache = accountBalanceCache;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _homeSummaryCache = homeSummaryCache;
        _accountFactDataService = accountFactDataService;
    }

    public async Task<FinancialDashboardDto> GetDashboardDataAsync(string accountFund, string userId)
    {
        if (!await IsAuthorizedForDashboardLoadAsync(accountFund, userId))
            return new FinancialDashboardDto();
        return await GetDashboardDataInternalAsync(accountFund, userId, useSessionAccountDataCache: true, includeEmbeddedPayload: false);
    }

    public async Task<FinancialDashboardDto> GetDashboardSummaryDataAsync(string accountFund, string userId)
    {
        if (!await IsAuthorizedForDashboardLoadAsync(accountFund, userId))
            return new FinancialDashboardDto();
        return await GetDashboardDataInternalAsync(accountFund, userId, useSessionAccountDataCache: true, includeEmbeddedPayload: false);
    }

    public async Task WarmHomeSummaryFromDataAsync(Account account, CacheRebuildData data, CancellationToken cancellationToken = default)
    {
        var contextAccount = ToContextAccount(account);
        if (!data.DonationsByAccountId.TryGetValue(account.AccountId, out var donations))
        {
            donations = [];
        }

        var accounting = data.AccountingByAccountId.TryGetValue(account.AccountId, out var rows)
            ? rows
            : [];
        var cachedData = new DashboardAccountCacheData
        {
            Fund = contextAccount.Fund,
            WindowStart = GetHomeWindowStart(DateTime.Now),
            WindowEnd = GetHomeWindowEnd(DateTime.Now),
            DonationData = donations,
            SubAccounts = data.SubAccounts.Where(subAccount => subAccount.AccountId == account.AccountId).ToList(),
            AccountingData = accounting
        };
        var now = DateTime.Now;
        var generation = _cacheInvalidationVersion.GetGeneration(CacheDataDomains.Donations | CacheDataDomains.Accounting | CacheDataDomains.AccountMetadata);
        var key = new HomeSummaryCacheKey(account.AccountId, account.Fund, string.Empty, generation, now.Year, now.Month).Normalize();
        await _homeSummaryCache.GetOrCreateAsync(key, token => BuildHomeSummaryCacheEntryAsync(contextAccount, cachedData, now, token), cancellationToken);
    }

    public async Task WarmInternSummaryFromDataAsync(string internName, CacheRebuildData data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(internName))
        {
            return;
        }

        var account = new UserAccountContextAccount { InternName = internName };
        var donations = data.DonationsByInternName.TryGetValue(internName, out var rows) ? rows : [];
        var donationData = new DonationCalculationData { Donations = donations };
        var now = DateTime.Now;
        var generation = _cacheInvalidationVersion.GetGeneration(CacheDataDomains.Donations | CacheDataDomains.AccountMetadata);
        var key = new HomeSummaryCacheKey(0, string.Empty, internName, generation, now.Year, now.Month).Normalize();
        await _homeSummaryCache.GetOrCreateAsync(
            key,
            token => Task.FromResult(BuildInternSummaryCacheEntry(account, donationData, now, token)),
            cancellationToken);
    }

    private async Task<bool> IsAuthorizedForDashboardLoadAsync(string accountFund, string userId)
    {
        if (string.IsNullOrWhiteSpace(accountFund))
        {
            return (await _authorizationService.ValidateUserAsync(userId)).IsAuthorized;
        }

        return (await _authorizationService.ValidateAccountAccessAsync(userId, accountFund)).IsAuthorized;
    }

    private HomeSummaryCacheEntry BuildInternSummaryCacheEntry(
        UserAccountContextAccount account,
        DonationCalculationData donationData,
        DateTime now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        var priorMonthStart = currentMonthStart.AddMonths(-1);
        var priorMonthEnd = currentMonthStart.AddDays(-1);
        var currentYearStart = new DateTime(now.Year, 1, 1);
        var currentYearEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var priorYearStart = new DateTime(now.Year - 1, 1, 1);
        var priorYearEnd = new DateTime(now.Year - 1, 12, 31);
        return new HomeSummaryCacheEntry
        {
            CurrentMonth = BuildInternSummaryFromData(account, donationData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy")),
            PriorMonth = BuildInternSummaryFromData(account, donationData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy")),
            CurrentYear = BuildInternSummaryFromData(account, donationData, currentYearStart, currentYearEnd, now.ToString("yyyy")),
            PriorYear = BuildInternSummaryFromData(account, donationData, priorYearStart, priorYearEnd, (now.Year - 1).ToString())
        };
    }

    public async Task<FinancialSummaryDto> GetCustomSummaryAsync(
        string accountFund,
        DateTime startDate,
        DateTime endDate,
        string userId)
    {
        if (!(await _authorizationService.ValidateAccountAccessAsync(userId, accountFund)).IsAuthorized)
            return new FinancialSummaryDto { Period = $"{startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy}" };
        if (endDate.Date < startDate.Date)
        {
            throw new ArgumentException("The custom summary end date must be on or after the start date.");
        }

        var userContext = await _userAccountContextService.GetContextAsync(userId);
        var selectedAccount = userContext is null
            ? null
            : _userAccountContextService.ResolveSelectedAccount(userContext, accountFund);

        if (userContext is null || selectedAccount is null)
        {
            return new FinancialSummaryDto
            {
                Period = $"{startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy}"
            };
        }

        var now = DateTime.Now;
        var broadWindowStart = new DateTime(now.Year - 1, 1, 1);
        var broadWindowEnd = new DateTime(now.Year, 12, 31);
        var cachedData = await GetOrLoadAccountDataAsync(
            selectedAccount,
            startDate.Date >= broadWindowStart && endDate.Date <= broadWindowEnd ? broadWindowStart : startDate.Date,
            startDate.Date >= broadWindowStart && endDate.Date <= broadWindowEnd ? broadWindowEnd : endDate.Date,
            userId);

        var result = BuildSummaryFromCache(
            selectedAccount,
            cachedData,
            startDate.Date,
            endDate.Date,
            $"{startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy}");

        var endingBalanceTask = CalculateCachedHomeBalanceAsync(selectedAccount, endDate.Date, userId);
        var startingBalanceTask = CalculateCachedHomeBalanceAsync(selectedAccount, startDate.Date.AddDays(-1), userId);
        await Task.WhenAll(endingBalanceTask, startingBalanceTask);

        result.Summary.StartingBalance = startingBalanceTask.Result;
        result.Summary.Balance = endingBalanceTask.Result;

        return result.Summary;
    }

    private async Task<decimal> CalculateCachedHomeBalanceAsync(
        UserAccountContextAccount account,
        DateTime asOfDate,
        string userId = "")
    {
        var generation = _cacheInvalidationVersion.Current;
        if (_accountBalanceCache.TryGet(
                account.AccountId,
                account.AccountingClass,
                account.AccountNumber,
                account.BalanceAdjustment,
                generation,
                asOfDate,
                userId,
                out var cachedBalance))
        {
            return cachedBalance;
        }

        if (TryGetCachedBalanceAnchor(account, generation, out var anchorDate, out var anchorBalance))
        {
            var dataStart = new[] { anchorDate, asOfDate.Date }.Min();
            var dataEnd = new[] { anchorDate, asOfDate.Date }.Max();
            var accountingData = await _accountFactDataService.GetAccountingDataAsync(account, userId, dataStart, dataEnd);
            var anchored = _reverseBalanceCalculationService.CalculateFromEndOfDayAnchor(
                account,
                accountingData,
                anchorDate,
                anchorBalance,
                asOfDate);

            _accountBalanceCache.Set(
                account.AccountId,
                account.AccountingClass,
                account.AccountNumber,
                account.BalanceAdjustment,
                generation,
                asOfDate,
                userId,
                anchored.TotalBalance);
            return anchored.TotalBalance;
        }

        var calculated = await _accountCalculationService.CalculateBalanceAsync(account, userId, null, asOfDate);
        _accountBalanceCache.Set(
            account.AccountId,
            account.AccountingClass,
            account.AccountNumber,
            account.BalanceAdjustment,
            generation,
            asOfDate,
            userId,
            calculated.TotalBalance);
        return calculated.TotalBalance;
    }

    private bool TryGetCachedBalanceAnchor(
        UserAccountContextAccount account,
        long generation,
        out DateTime anchorDate,
        out decimal anchorBalance)
    {
        var key = new HomeSummaryCacheKey(
            account.AccountId,
            account.Fund,
            account.InternName,
            generation,
            DateTime.Now.Year,
            DateTime.Now.Month).Normalize();
        if (_homeSummaryCache.TryGet(key, out var summaries))
        {
            anchorDate = new DateTime(DateTime.Now.Year - 1, 1, 1).AddDays(-1);
            anchorBalance = summaries.PriorYear.StartingBalance;
            return true;
        }

        anchorDate = default;
        anchorBalance = default;
        return false;
    }

    private async Task<FinancialDashboardDto> GetDashboardDataInternalAsync(
        string accountFund,
        string userId,
        bool useSessionAccountDataCache,
        bool includeEmbeddedPayload)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Dashboard load started: user={UserId}, mode={Mode}", userId, useSessionAccountDataCache ? "complete-cache" : "summary-direct");
        try
        {
            var dashboard = new FinancialDashboardDto();
            _logger.LogInformation(
                "Dashboard load phase=context-start elapsedMs={ElapsedMs}",
                stopwatch.ElapsedMilliseconds);
            var userContext = await _userAccountContextService.GetContextAsync(userId);
            _logger.LogInformation(
                "Dashboard load phase=context-complete elapsedMs={ElapsedMs} accountCount={AccountCount}",
                stopwatch.ElapsedMilliseconds,
                userContext?.Accounts?.Count ?? 0);
            if (userContext == null)
            {
                _logger.LogWarning("Dashboard user context could not be resolved for user identifier '{UserId}'", userId);
                return dashboard;
            }

            var accounts = userContext.Accounts;
            dashboard.UserAccounts = accounts
                .Select(a => new UserAccountDto
                {
                    AccountId = a.AccountId,
                    Fund = a.Fund ?? string.Empty,
                    InternName = userContext.IsIntern ? userContext.InternName : a.InternName ?? string.Empty,
                    DisplayName = userContext.IsIntern
                        ? userContext.InternName
                        : string.IsNullOrWhiteSpace(a.InternName)
                        ? InternAccountUtility.GetDisplayFundName(a.Fund)
                        : a.InternName,
                    AccountingClass = a.AccountingClass ?? string.Empty,
                    AccountNumber = a.AccountNumber ?? string.Empty,
                    Overhead = Convert.ToDecimal(a.Overhead),
                    IsDefault = userContext.DefaultAccountId.HasValue && a.AccountId == userContext.DefaultAccountId.Value
                })
                .GroupBy(a => a.SelectionKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (!dashboard.UserAccounts.Any())
            {
                _logger.LogWarning("No dashboard accounts found for user '{UserId}'", userId);
                return dashboard;
            }

            var selectedContextAccount = _userAccountContextService.ResolveSelectedAccount(userContext, accountFund);
            _logger.LogInformation(
                "Dashboard load phase=account-resolved elapsedMs={ElapsedMs} accountCount={AccountCount}",
                stopwatch.ElapsedMilliseconds,
                dashboard.UserAccounts.Count);
            if (selectedContextAccount == null)
            {
                return dashboard;
            }

            var isInternAccount = userContext.IsIntern || !string.IsNullOrWhiteSpace(selectedContextAccount.InternName);
            dashboard.SelectedAccount = isInternAccount
                ? string.IsNullOrWhiteSpace(selectedContextAccount.InternName)
                    ? userContext.InternName
                    : selectedContextAccount.InternName
                : selectedContextAccount.Fund ?? string.Empty;
            dashboard.HasAccountData = true;

            // Full-range donation payloads are only needed by complete dashboard loads.
            // Summary loads keep the initial Home request small while snapshot warmup prepares
            // the data for Donations, Expenses, and Donors in the background.
            if (includeEmbeddedPayload)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var start = new DateTime(now.Year - 2, 1, 1);
                    var end = new DateTime(now.Year, 12, 31);

                    if (!string.IsNullOrWhiteSpace(dashboard.SelectedAccount))
                    {
                        try
                        {
                            var selDto = await _donationService.GetDonationDataAsync(
                                dashboard.SelectedAccount,
                                "All",
                                new Core.ValueObjects.DateRange(start, end),
                                userId);
                            dashboard.SelectedAccountDonations = selDto;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to preload donations for selected account {Account}", dashboard.SelectedAccount);
                        }
        finally
        {
            _logger.LogInformation("Dashboard load completed: user={UserId}, mode={Mode}, elapsedMs={ElapsedMs}", userId, useSessionAccountDataCache ? "complete-cache" : "summary-direct", stopwatch.ElapsedMilliseconds);
        }
                    }

                    var defaultAcc = dashboard.UserAccounts.FirstOrDefault(a => a.IsDefault)?.Fund;
                    if (!string.IsNullOrWhiteSpace(defaultAcc) && !string.Equals(defaultAcc, dashboard.SelectedAccount, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var defDto = await _donationService.GetDonationDataAsync(defaultAcc, "All", new Core.ValueObjects.DateRange(start, end), userId);
                            dashboard.DefaultAccountDonations = defDto;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to preload donations for default account {Account}", defaultAcc);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error preloading embedded donation payloads for dashboard");
                }
            }

            if (isInternAccount)
            {
                await PopulateInternSummariesAsync(dashboard, selectedContextAccount, userId);
            }
            else if (useSessionAccountDataCache)
            {
                await PopulateSummariesFromSnapshotAsync(dashboard, selectedContextAccount, userId);
            }
            else
            {
                await PopulateSummariesDirectAsync(dashboard, selectedContextAccount, userId);
            }

            _logger.LogInformation(
                "Dashboard load phase=complete elapsedMs={ElapsedMs} summaryCacheMode={SummaryCacheMode}",
                stopwatch.ElapsedMilliseconds,
                useSessionAccountDataCache ? "shared" : "direct");

            return dashboard;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data");
            return new FinancialDashboardDto();
        }
    }

    public async Task<List<UserAccountDto>> GetUserAccountsAsync(string userId)
    {
        try
        {
            var userContext = await _userAccountContextService.GetContextAsync(userId);
            if (userContext == null)
            {
                return new List<UserAccountDto>();
            }

            return userContext.Accounts.Select(a => new UserAccountDto
            {
                AccountId = a.AccountId,
                Fund = a.Fund ?? string.Empty,
                InternName = userContext.IsIntern ? userContext.InternName : a.InternName ?? string.Empty,
                DisplayName = userContext.IsIntern
                    ? userContext.InternName
                    : string.IsNullOrWhiteSpace(a.InternName)
                    ? InternAccountUtility.GetDisplayFundName(a.Fund)
                    : a.InternName,
                AccountingClass = a.AccountingClass ?? string.Empty,
                AccountNumber = a.AccountNumber ?? string.Empty,
                Overhead = Convert.ToDecimal(a.Overhead),
                IsDefault = userContext.DefaultAccountId.HasValue && a.AccountId == userContext.DefaultAccountId.Value
            }).GroupBy(a => a.SelectionKey, StringComparer.OrdinalIgnoreCase)
              .Select(group => group.First())
              .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user accounts for '{UserId}'", userId);
            return new List<UserAccountDto>();
        }
    }

    public async Task<bool> ValidateAccountAccessAsync(string accountFund, string userId)
    {
        var userAccounts = await GetUserAccountsAsync(userId);
        return userAccounts.Any(a =>
            a.SelectionKey.Equals(accountFund, StringComparison.OrdinalIgnoreCase) ||
            a.Fund.Equals(accountFund, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<List<MonthlyAccountVisualizationDto>> GetMonthlyVisualizationAsync(string accountFund, DateTime startDate, DateTime endDate, string userId)
    {
        var points = new List<MonthlyAccountVisualizationDto>();

        try
        {
            var userContext = await _userAccountContextService.GetContextAsync(userId);
            if (userContext == null)
            {
                return points;
            }

            var selectedAccount = _userAccountContextService.ResolveSelectedAccount(userContext, accountFund);
            if (selectedAccount == null)
            {
                return points;
            }

            if (InternAccountUtility.IsInternFund(selectedAccount.Fund) &&
                InternAccountUtility.TryGetInternDesignationName(selectedAccount.Fund, out var internDesignationName))
            {
                var monthlyDonationData = await _accountCalculationService.LoadDonationCalculationDataAsync(selectedAccount, userId, startDate, endDate);
                var internCursor = new DateTime(startDate.Year, startDate.Month, 1);
                var internEndMonth = new DateTime(endDate.Year, endDate.Month, 1);
                var internSingleYear = startDate.Year == endDate.Year;

                while (internCursor <= internEndMonth)
                {
                    var monthStart = internCursor;
                    var monthEnd = internCursor.AddMonths(1).AddDays(-1);
                    if (monthEnd > endDate)
                    {
                        monthEnd = endDate;
                    }

                    var donationTotal = _accountCalculationService.CalculateDonationTotalsFromData(
                        selectedAccount,
                        monthlyDonationData.Donations,
                        monthlyDonationData.SubAccounts,
                        monthStart,
                        monthEnd).TotalDonations;
                    points.Add(new MonthlyAccountVisualizationDto
                    {
                        MonthStart = monthStart,
                        MonthLabel = monthStart.ToString(internSingleYear ? "MMM" : "MMM yy"),
                        DonationTotal = donationTotal,
                        OverheadTotal = _accountCalculationService.CalculateOverheadAmount(selectedAccount, donationTotal),
                        ExpenseTotal = 0,
                        Balance = donationTotal
                    });

                    internCursor = internCursor.AddMonths(1);
                }

                return points;
            }

            var donationData = await _accountCalculationService.LoadDonationCalculationDataAsync(selectedAccount, userId, startDate, endDate);
            var cursor = new DateTime(startDate.Year, startDate.Month, 1);
            var endMonth = new DateTime(endDate.Year, endDate.Month, 1);
            var singleYear = startDate.Year == endDate.Year;

            while (cursor <= endMonth)
            {
                var monthStart = cursor;
                var monthEnd = cursor.AddMonths(1).AddDays(-1);
                if (monthEnd > endDate)
                {
                    monthEnd = endDate;
                }

                var donationTotal = _accountCalculationService.CalculateDonationTotalsFromData(
                    selectedAccount,
                    donationData.Donations,
                    donationData.SubAccounts,
                    monthStart,
                    monthEnd).TotalDonations;
                var expenseTask = _accountCalculationService.CalculateBalanceAsync(selectedAccount, userId, monthStart, monthEnd);
                var balanceTask = CalculateCachedHomeBalanceAsync(selectedAccount, monthEnd, userId);

                await Task.WhenAll(expenseTask, balanceTask);

                points.Add(new MonthlyAccountVisualizationDto
                {
                    MonthStart = monthStart,
                    MonthLabel = monthStart.ToString(singleYear ? "MMM" : "MMM yy"),
                    DonationTotal = donationTotal,
                    OverheadTotal = _accountCalculationService.CalculateOverheadAmount(selectedAccount, donationTotal),
                    ExpenseTotal = expenseTask.Result.ExpenseTotal,
                    Balance = balanceTask.Result
                });

                cursor = cursor.AddMonths(1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building monthly visualization");
        }

        return points;
    }

    private async Task PopulateInternSummariesAsync(FinancialDashboardDto dashboard, UserAccountContextAccount selectedAccount, string userId)
    {
        if (string.IsNullOrWhiteSpace(selectedAccount.InternName))
        {
            return;
        }

        var now = DateTime.Now;
        var generation = _cacheInvalidationVersion.GetGeneration(CacheDataDomains.Donations | CacheDataDomains.AccountMetadata);
        var cacheKey = new HomeSummaryCacheKey(
            selectedAccount.AccountId,
            string.Empty,
            selectedAccount.InternName,
            generation,
            now.Year,
            now.Month).Normalize();
        var summaries = await GetOrCreateInternSummaryAsync(selectedAccount, userId, now, CancellationToken.None);

        dashboard.CurrentMonth = summaries.CurrentMonth;
        dashboard.PriorMonth = summaries.PriorMonth;
        dashboard.CurrentYear = summaries.CurrentYear;
        dashboard.PriorYear = summaries.PriorYear;

        SetYearAverages(dashboard, now);
    }

    private Task<HomeSummaryCacheEntry> GetOrCreateInternSummaryAsync(
        UserAccountContextAccount account,
        string userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var generation = _cacheInvalidationVersion.GetGeneration(CacheDataDomains.Donations | CacheDataDomains.AccountMetadata);
        var cacheKey = new HomeSummaryCacheKey(
            account.AccountId,
            string.Empty,
            account.InternName,
            generation,
            now.Year,
            now.Month).Normalize();
        return _homeSummaryCache.GetOrCreateAsync(
            cacheKey,
            token => BuildInternSummaryCacheEntryAsync(account, userId, now, token),
            cancellationToken);
    }

    private async Task<HomeSummaryCacheEntry> BuildInternSummaryCacheEntryAsync(
        UserAccountContextAccount account,
        string userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        var priorMonthStart = currentMonthStart.AddMonths(-1);
        var priorMonthEnd = currentMonthStart.AddDays(-1);
        var currentYearStart = new DateTime(now.Year, 1, 1);
        var currentYearEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var priorYearStart = new DateTime(now.Year - 1, 1, 1);
        var priorYearEnd = new DateTime(now.Year - 1, 12, 31);
        var donationData = await _accountCalculationService.LoadDonationCalculationDataAsync(
            account,
            userId,
            priorYearStart,
            currentYearEnd);

        return new HomeSummaryCacheEntry
        {
            CurrentMonth = BuildInternSummaryFromData(account, donationData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy")),
            PriorMonth = BuildInternSummaryFromData(account, donationData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy")),
            CurrentYear = BuildInternSummaryFromData(account, donationData, currentYearStart, currentYearEnd, now.ToString("yyyy")),
            PriorYear = BuildInternSummaryFromData(account, donationData, priorYearStart, priorYearEnd, (now.Year - 1).ToString())
        };
    }

    private FinancialSummaryDto BuildInternSummaryFromData(UserAccountContextAccount account, DonationCalculationData donationData, DateTime startDate, DateTime endDate, string period)
    {
        var donationTotal = _accountCalculationService.CalculateDonationTotalsFromData(
            account,
            donationData.Donations,
            donationData.SubAccounts,
            startDate,
            endDate).TotalDonations;

        return new FinancialSummaryDto
        {
            Period = period,
            TotalDonations = donationTotal,
            PrimaryDonations = donationTotal,
            TotalOverhead = _accountCalculationService.CalculateOverheadAmount(account, donationTotal),
            TotalExpenses = 0,
            InternalTransfers = 0,
            Balance = donationTotal
        };
    }

    private async Task PopulateSummariesFromSnapshotAsync(FinancialDashboardDto dashboard, UserAccountContextAccount selectedAccount, string userId)
    {
        var now = DateTime.Now;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        var priorMonthStart = currentMonthStart.AddMonths(-1);
        var priorMonthEnd = currentMonthStart.AddDays(-1);
        var currentYearStart = new DateTime(now.Year, 1, 1);
        var currentYearEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var priorYearStart = new DateTime(now.Year - 1, 1, 1);
        var priorYearEnd = new DateTime(now.Year - 1, 12, 31);

        var windowStart = new DateTime(now.Year - 1, 1, 1);
        var windowEnd = new DateTime(now.Year, 12, 31);

        var generation = _cacheInvalidationVersion.GetGeneration(
            CacheDataDomains.Donations | CacheDataDomains.Accounting | CacheDataDomains.AccountMetadata);
        var isInternAccount = InternAccountUtility.IsInternFund(selectedAccount.Fund);
        var cacheKey = isInternAccount
            ? new HomeSummaryCacheKey(0, string.Empty, selectedAccount.InternName, generation, now.Year, now.Month).Normalize()
            : new HomeSummaryCacheKey(selectedAccount.AccountId, selectedAccount.Fund, string.Empty, generation, now.Year, now.Month).Normalize();

        HomeSummaryCacheEntry cachedSummaries;
        if (_homeSummaryCache.TryGet(cacheKey, out var existingSummaries))
        {
            _logger.LogInformation(
                "Home summary cache hit: accountId={AccountId}, intern={IsIntern}, generation={Generation}, year={Year}, month={Month}",
                selectedAccount.AccountId,
                isInternAccount,
                generation,
                now.Year,
                now.Month);
            cachedSummaries = existingSummaries;
        }
        else
        {
            _logger.LogInformation(
                "Home summary cache miss: accountId={AccountId}, intern={IsIntern}, generation={Generation}, year={Year}, month={Month}",
                selectedAccount.AccountId,
                isInternAccount,
                generation,
                now.Year,
                now.Month);
            cachedSummaries = await _homeSummaryCache.GetOrCreateAsync(
                cacheKey,
                async cancellationToken =>
                {
                    var cachedData = await GetOrLoadAccountDataAsync(selectedAccount, windowStart, windowEnd);
                    return await BuildHomeSummaryCacheEntryAsync(selectedAccount, cachedData, now, cancellationToken);
                });
        }

        dashboard.CurrentMonth = cachedSummaries.CurrentMonth;
        dashboard.PriorMonth = cachedSummaries.PriorMonth;
        dashboard.CurrentYear = cachedSummaries.CurrentYear;
        dashboard.PriorYear = cachedSummaries.PriorYear;

        SetYearAverages(dashboard, now);
    }

    private async Task<HomeSummaryCacheEntry> BuildHomeSummaryCacheEntryAsync(
        UserAccountContextAccount account,
        DashboardAccountCacheData cachedData,
        DateTime now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        var priorMonthStart = currentMonthStart.AddMonths(-1);
        var priorMonthEnd = currentMonthStart.AddDays(-1);
        var currentYearStart = new DateTime(now.Year, 1, 1);
        var currentYearEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var priorYearStart = new DateTime(now.Year - 1, 1, 1);
        var priorYearEnd = new DateTime(now.Year - 1, 12, 31);

        var currentMonth = BuildSummaryFromCache(account, cachedData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        var priorMonth = BuildSummaryFromCache(account, cachedData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        var currentYear = BuildSummaryFromCache(account, cachedData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        var priorYear = BuildSummaryFromCache(account, cachedData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

        await PopulateAsOfBalancesAsync(
            account,
            cachedData,
            (currentMonth.Summary, currentMonthStart, currentMonthEnd),
            (priorMonth.Summary, priorMonthStart, priorMonthEnd),
            (currentYear.Summary, currentYearStart, currentYearEnd),
            (priorYear.Summary, priorYearStart, priorYearEnd));

        return new HomeSummaryCacheEntry
        {
            CurrentMonth = currentMonth.Summary,
            PriorMonth = priorMonth.Summary,
            CurrentYear = currentYear.Summary,
            PriorYear = priorYear.Summary
        };
    }

    private async Task<DashboardAccountCacheData> GetOrLoadAccountDataAsync(
        UserAccountContextAccount account,
        DateTime windowStart,
        DateTime windowEnd,
        string userId = "")
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var donationData = await _accountFactDataService.GetDonationDataAsync(account, userId, windowStart, windowEnd);
            var accountingData = await _accountFactDataService.GetAccountingDataAsync(account, userId, windowStart, windowEnd);
            return new DashboardAccountCacheData
            {
                Fund = account.Fund,
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                DonationData = donationData.Donations,
                SubAccounts = donationData.SubAccounts,
                AccountingData = accountingData
            };
        }

        var range = new Cya2.Core.ValueObjects.DateRange(windowStart, windowEnd);
        var donationKey = new DonationDataCacheKey(account.AccountId, _cacheInvalidationVersion.Donations, windowStart, windowEnd).Normalize();
        var donationEntry = await _donationDataCache.GetOrCreateAsync(
            donationKey,
            cancellationToken => _donationDataCacheLoader.LoadAsync(account, range, donationKey, cancellationToken));
        var accountingKey = new AccountingDataCacheKey(account.AccountId, account.AccountingClass, account.AccountNumber, _cacheInvalidationVersion.Accounting, windowStart, windowEnd).Normalize();
        var accountingEntry = await _accountingDataCache.GetOrCreateAsync(
            accountingKey,
            cancellationToken => _accountingDataCacheLoader.LoadAsync(account, range, accountingKey, cancellationToken));

        return new DashboardAccountCacheData
        {
            Fund = account.Fund,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            AccountingData = accountingEntry.Transactions.Select(record => new AccountingRecord
            {
                Id = record.Id,
                AccountingClass = record.AccountingClass,
                Date = record.Date,
                Num = record.Number,
                Amount = record.Amount,
                AccountNumber = record.AccountNumber,
                Account = record.Account,
                Type = record.Type,
                DateCreated = DateTime.MinValue
            }).ToList(),
            DonationData = donationEntry.Donations.Select(record => new DonationRecord
            {
                Id = record.Id,
                DonorId = record.DonorId,
                DonorDisplayName = record.DonorDisplayName,
                Date = record.Date,
                Frequency = record.Frequency,
                AccountName = record.AccountName,
                Amount = record.Amount,
                Fund = record.Fund,
                Addressee = record.Addressee,
                SoftCreditName = record.SoftCreditName,
                IsAnonymous = record.IsAnonymous
            }).ToList(),
            SubAccounts = donationEntry.SubAccounts.Select(record => new SubAccount
            {
                Id = record.Id,
                AccountId = record.AccountId,
                SubFund = record.SubFund,
                Kind = record.Kind
            }).ToList()
        };
    }

    private async Task PopulateAsOfBalancesAsync(
        UserAccountContextAccount account,
        DashboardAccountCacheData cachedData,
        params (FinancialSummaryDto Summary, DateTime Start, DateTime End)[] periods)
    {
        var tasks = periods.Select(async period =>
        {
            var startingBalance = _accountCalculationService.CalculateBalanceFromData(
                account,
                cachedData.AccountingData,
                null,
                period.Start.AddDays(-1)).TotalBalance;
            var endingBalance = _accountCalculationService.CalculateBalanceFromData(
                account,
                cachedData.AccountingData,
                null,
                period.End).TotalBalance;
            period.Summary.StartingBalance = startingBalance;
            period.Summary.Balance = endingBalance;
            await Task.CompletedTask;
        });

        await Task.WhenAll(tasks);
    }

    private async Task PopulateSummariesDirectAsync(FinancialDashboardDto dashboard, UserAccountContextAccount selectedAccount, string userId)
    {
        var now = DateTime.Now;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
        var priorMonthStart = currentMonthStart.AddMonths(-1);
        var priorMonthEnd = currentMonthStart.AddDays(-1);
        var currentYearStart = new DateTime(now.Year, 1, 1);
        var currentYearEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var priorYearStart = new DateTime(now.Year - 1, 1, 1);
        var priorYearEnd = new DateTime(now.Year - 1, 12, 31);
        var donationData = await _accountCalculationService.LoadDonationCalculationDataAsync(
            selectedAccount,
            userId,
            priorYearStart,
            currentYearEnd);

        var currentMonthTask = BuildSummaryFromAggregatesAsync(selectedAccount, userId, donationData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        var priorMonthTask = BuildSummaryFromAggregatesAsync(selectedAccount, userId, donationData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        var currentYearTask = BuildSummaryFromAggregatesAsync(selectedAccount, userId, donationData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        var priorYearTask = BuildSummaryFromAggregatesAsync(selectedAccount, userId, donationData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

        await Task.WhenAll(currentMonthTask, priorMonthTask, currentYearTask, priorYearTask);

        var currentMonth = await currentMonthTask;
        var priorMonth = await priorMonthTask;
        var currentYear = await currentYearTask;
        var priorYear = await priorYearTask;

        dashboard.CurrentMonth = currentMonth.Summary;
        dashboard.PriorMonth = priorMonth.Summary;
        dashboard.CurrentYear = currentYear.Summary;
        dashboard.PriorYear = priorYear.Summary;
        LogHomeOtherAccounts(selectedAccount, currentMonth.OtherAccounts
            .Concat(priorMonth.OtherAccounts)
            .Concat(currentYear.OtherAccounts)
            .Concat(priorYear.OtherAccounts));

        SetYearAverages(dashboard, now);
    }

    private async Task<HomeSummaryResult> BuildSummaryFromAggregatesAsync(UserAccountContextAccount account, string userId, DonationCalculationData donationData, DateTime startDate, DateTime endDate, string period)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var donationTotals = _accountCalculationService.CalculateDonationTotalsFromData(
            account,
            donationData.Donations,
            donationData.SubAccounts,
            startDate,
            endDate);
        var expenseTask = _accountCalculationService.CalculateBalanceAsync(account, userId, startDate, endDate);
        var balanceTask = CalculateCachedHomeBalanceAsync(account, endDate, userId);
        var startingBalanceTask = CalculateCachedHomeBalanceAsync(account, startDate.AddDays(-1), userId);
        await Task.WhenAll(expenseTask, balanceTask, startingBalanceTask);

        var balanceCalculation = expenseTask.Result;
        var expenseTotal = balanceCalculation.ExpenseTotal;
        var transferTotal = balanceCalculation.TransferTotal;
        var balance = balanceTask.Result;

        return new HomeSummaryResult
        {
            Summary = new FinancialSummaryDto
            {
                Period = period,
                TotalDonations = donationTotals.TotalDonations,
                PrimaryDonations = donationTotals.PrimaryDonations,
                TotalOverhead = donationTotals.OverheadTotal,
                TotalExpenses = expenseTotal,
                InternalTransfers = transferTotal,
                SeparateSubfundTotals = new Dictionary<string, decimal>(donationTotals.SeparateSubfundTotals, StringComparer.OrdinalIgnoreCase),
                StartingBalance = startingBalanceTask.Result,
                Balance = balance
            },
            OtherAccounts = balanceCalculation.OtherTransactions
                .Select(transaction => transaction.Account ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private HomeSummaryResult BuildSummaryFromCache(UserAccountContextAccount account, DashboardAccountCacheData cachedData, DateTime startDate, DateTime endDate, string period)
    {
        var summary = new FinancialSummaryDto { Period = period };

        var donationTotals = _accountCalculationService.CalculateDonationTotalsFromData(
            account,
            cachedData.DonationData,
            cachedData.SubAccounts,
            startDate,
            endDate);

        var periodBalance = _accountCalculationService.CalculateBalanceFromData(
            account,
            cachedData.AccountingData,
            startDate,
            endDate);

        var expenseTotal = periodBalance.ExpenseTotal;
        summary.TotalDonations = donationTotals.TotalDonations;
        summary.PrimaryDonations = donationTotals.PrimaryDonations;
        summary.TotalOverhead = donationTotals.OverheadTotal;
        summary.SeparateSubfundTotals = new Dictionary<string, decimal>(donationTotals.SeparateSubfundTotals, StringComparer.OrdinalIgnoreCase);
        summary.TotalExpenses = expenseTotal;
        summary.InternalTransfers = periodBalance.TransferTotal;
        return new HomeSummaryResult
        {
            Summary = summary,
            OtherAccounts = periodBalance.OtherTransactions
                .Select(transaction => transaction.Account ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private void LogHomeOtherAccounts(UserAccountContextAccount account, IEnumerable<string> otherAccounts)
    {
        var distinctAccounts = otherAccounts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

        _logger.LogInformation(
            "Home balance account values: accountId={AccountId}, otherAccountCount={OtherAccountCount}",
            account.AccountId,
            distinctAccounts.Count);
    }

    private static DateTime GetHomeWindowStart(DateTime now) => new(now.Year - 1, 1, 1);

    private static DateTime GetHomeWindowEnd(DateTime now) => now.Date;

    private static UserAccountContextAccount ToContextAccount(Account account) => new()
    {
        AccountId = account.AccountId,
        Fund = account.Fund ?? string.Empty,
        AccountingClass = account.AccountingClass ?? string.Empty,
        AccountNumber = account.AccountNumber ?? string.Empty,
        CreatedAt = account.CreatedAt,
        Overhead = account.Overhead,
        SoftCredit = account.SoftCredit ?? string.Empty,
        BalanceAdjustment = account.BalanceAdjustment,
        OtherFunds = account.OtherFunds
    };

    private sealed class HomeSummaryResult
    {
        public FinancialSummaryDto Summary { get; init; } = new();
        public List<string> OtherAccounts { get; init; } = new();
    }

    private static void SetYearAverages(FinancialDashboardDto dashboard, DateTime now)
    {
        var monthsElapsed = Math.Max(1, now.Month);
        dashboard.CurrentYear.AvgMonthlyDonations = Math.Round(dashboard.CurrentYear.TotalDonations / monthsElapsed, 2);
        dashboard.CurrentYear.AvgMonthlyExpenses = Math.Round(dashboard.CurrentYear.TotalExpenses / monthsElapsed, 2);
        dashboard.PriorYear.AvgMonthlyDonations = Math.Round(dashboard.PriorYear.TotalDonations / 12m, 2);
        dashboard.PriorYear.AvgMonthlyExpenses = Math.Round(dashboard.PriorYear.TotalExpenses / 12m, 2);
    }
}