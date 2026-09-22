using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class FinancialDashboardService : IFinancialDashboardService
{
    private readonly ILogger<FinancialDashboardService> _logger;
    private readonly IAccountCalculationService _accountCalculationService;
    private readonly ISessionAccountDataCacheService _sessionAccountDataCache;
    private readonly IUserAccountContextService _userAccountContextService;
    private readonly IDonationService _donationService;

    public FinancialDashboardService(
        ILogger<FinancialDashboardService> logger,
        IAccountCalculationService accountCalculationService,
        ISessionAccountDataCacheService sessionAccountDataCache,
        IUserAccountContextService userAccountContextService,
        IDonationService donationService)
    {
        _logger = logger;
        _accountCalculationService = accountCalculationService;
        _sessionAccountDataCache = sessionAccountDataCache;
        _userAccountContextService = userAccountContextService;
        _donationService = donationService;
    }

    public async Task<FinancialDashboardDto> GetDashboardDataAsync(string accountFund, string userId)
    {
        return await GetDashboardDataInternalAsync(accountFund, userId, useSessionAccountDataCache: true);
    }

    public async Task<FinancialDashboardDto> GetDashboardSummaryDataAsync(string accountFund, string userId)
    {
        return await GetDashboardDataInternalAsync(accountFund, userId, useSessionAccountDataCache: false);
    }

    public async Task<FinancialSummaryDto> GetCustomSummaryAsync(
        string accountFund,
        DateTime startDate,
        DateTime endDate,
        string userId)
    {
        if (endDate.Date < startDate.Date)
        {
            throw new ArgumentException("The custom summary end date must be on or after the start date.");
        }

        var userContext = await _userAccountContextService.GetContextAsync(userId);
        var selectedAccount = userContext is null
            ? null
            : _userAccountContextService.ResolveSelectedAccount(userContext, accountFund);

        if (selectedAccount is null)
        {
            return new FinancialSummaryDto
            {
                Period = $"{startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy}"
            };
        }

        var now = DateTime.Now;
        var broadWindowStart = new DateTime(now.Year - 1, 1, 1);
        var broadWindowEnd = new DateTime(now.Year, 12, 31);
        var isDefaultAccount = userContext.DefaultAccountId.HasValue &&
                               userContext.DefaultAccountId.Value == selectedAccount.AccountId;
        var cachedData = await _sessionAccountDataCache.GetOrLoadAccountDataAsync(
            selectedAccount,
            startDate.Date >= broadWindowStart && endDate.Date <= broadWindowEnd ? broadWindowStart : startDate.Date,
            startDate.Date >= broadWindowStart && endDate.Date <= broadWindowEnd ? broadWindowEnd : endDate.Date,
            isDefaultAccount);

        var result = BuildSummaryFromCache(
            selectedAccount,
            cachedData,
            startDate.Date,
            endDate.Date,
            $"{startDate:MM/dd/yyyy} - {endDate:MM/dd/yyyy}");

        var endingBalanceTask = _accountCalculationService.CalculateBalanceAsync(
            selectedAccount,
            null,
            endDate.Date);
        var startingBalanceTask = _accountCalculationService.CalculateBalanceAsync(
            selectedAccount,
            null,
            startDate.Date.AddDays(-1));
        await Task.WhenAll(endingBalanceTask, startingBalanceTask);

        result.Summary.StartingBalance = startingBalanceTask.Result.TotalBalance;
        result.Summary.Balance = endingBalanceTask.Result.TotalBalance;

        return result.Summary;
    }

    private async Task<FinancialDashboardDto> GetDashboardDataInternalAsync(string accountFund, string userId, bool useSessionAccountDataCache)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Dashboard load started: user={UserId}, fund={Fund}, mode={Mode}", userId, accountFund, useSessionAccountDataCache ? "complete-cache" : "summary-direct");
        try
        {
            var dashboard = new FinancialDashboardDto();
            // _logger.LogInformation("Dashboard load phase=context-start user={UserId} accountFund={AccountFund} summaryOnly={SummaryOnly}", userId, accountFund, !useSessionAccountDataCache);
            var userContext = await _userAccountContextService.GetContextAsync(userId);
            // _logger.LogInformation("Dashboard load phase=context-complete elapsedMs={ElapsedMs} accounts={AccountCount}", stopwatch.ElapsedMilliseconds, userContext?.Accounts?.Count ?? 0);
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
                    DisplayName = InternAccountUtility.GetDisplayFundName(a.Fund),
                    AccountingClass = a.AccountingClass ?? string.Empty,
                    AccountNumber = a.AccountNumber ?? string.Empty,
                    Overhead = Convert.ToDecimal(a.Overhead),
                    IsDefault = userContext.DefaultAccountId.HasValue && a.AccountId == userContext.DefaultAccountId.Value
                })
                .ToList();

            if (!dashboard.UserAccounts.Any())
            {
                _logger.LogWarning("No dashboard accounts found for user '{UserId}'", userId);
                return dashboard;
            }

            var selectedContextAccount = _userAccountContextService.ResolveSelectedAccount(userContext, accountFund);
            if (selectedContextAccount == null)
            {
                return dashboard;
            }

            dashboard.SelectedAccount = selectedContextAccount.Fund ?? string.Empty;
            dashboard.HasAccountData = true;
            var isInternAccount = InternAccountUtility.IsInternFund(dashboard.SelectedAccount);

            // Full-range donation payloads are only needed by complete dashboard loads.
            // Summary loads keep the initial Home request small while snapshot warmup prepares
            // the data for Donations, Expenses, and Donors in the background.
            if (useSessionAccountDataCache)
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
            _logger.LogInformation("Dashboard load completed: user={UserId}, fund={Fund}, mode={Mode}, elapsedMs={ElapsedMs}", userId, accountFund, useSessionAccountDataCache ? "complete-cache" : "summary-direct", stopwatch.ElapsedMilliseconds);
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
                // _logger.LogInformation("Dashboard load phase=summary-start elapsedMs={ElapsedMs} account={Account} summaryType=intern", stopwatch.ElapsedMilliseconds, dashboard.SelectedAccount);
                await PopulateInternSummariesAsync(dashboard, selectedContextAccount);
            }
            else if (useSessionAccountDataCache)
            {
                await PopulateSummariesFromSessionCacheAsync(dashboard, selectedContextAccount, userContext.DefaultAccountId == selectedContextAccount.AccountId);
                _sessionAccountDataCache.LogCacheStatus();
            }
            else
            {
                // _logger.LogInformation("Dashboard load phase=summary-start elapsedMs={ElapsedMs} account={Account} summaryType=direct", stopwatch.ElapsedMilliseconds, dashboard.SelectedAccount);
                await PopulateSummariesDirectAsync(dashboard, selectedContextAccount);
            }

            // _logger.LogInformation("Dashboard load phase=complete elapsedMs={ElapsedMs} account={Account}", stopwatch.ElapsedMilliseconds, dashboard.SelectedAccount);

            return dashboard;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data for account '{AccountFund}'", accountFund);
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
                DisplayName = InternAccountUtility.GetDisplayFundName(a.Fund),
                AccountingClass = a.AccountingClass ?? string.Empty,
                AccountNumber = a.AccountNumber ?? string.Empty,
                Overhead = Convert.ToDecimal(a.Overhead),
                IsDefault = userContext.DefaultAccountId.HasValue && a.AccountId == userContext.DefaultAccountId.Value
            }).ToList();
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
        return userAccounts.Any(a => a.Fund.Equals(accountFund, StringComparison.OrdinalIgnoreCase));
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
                var monthlyDonationData = await _accountCalculationService.LoadDonationCalculationDataAsync(selectedAccount, startDate, endDate);
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

            var donationData = await _accountCalculationService.LoadDonationCalculationDataAsync(selectedAccount, startDate, endDate);
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
                var expenseTask = _accountCalculationService.CalculateBalanceAsync(selectedAccount, monthStart, monthEnd);
                var balanceTask = _accountCalculationService.CalculateBalanceAsync(selectedAccount, null, monthEnd);

                await Task.WhenAll(expenseTask, balanceTask);

                points.Add(new MonthlyAccountVisualizationDto
                {
                    MonthStart = monthStart,
                    MonthLabel = monthStart.ToString(singleYear ? "MMM" : "MMM yy"),
                    DonationTotal = donationTotal,
                    OverheadTotal = _accountCalculationService.CalculateOverheadAmount(selectedAccount, donationTotal),
                    ExpenseTotal = expenseTask.Result.ExpenseTotal,
                    Balance = balanceTask.Result.TotalBalance
                });

                cursor = cursor.AddMonths(1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building monthly visualization for account '{AccountFund}'", accountFund);
        }

        return points;
    }

    private async Task PopulateInternSummariesAsync(FinancialDashboardDto dashboard, UserAccountContextAccount selectedAccount)
    {
        if (!InternAccountUtility.TryGetInternDesignationName(selectedAccount.Fund, out var internDesignationName))
        {
            return;
        }

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
            priorYearStart,
            currentYearEnd);

        dashboard.CurrentMonth = BuildInternSummaryFromData(selectedAccount, donationData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        dashboard.PriorMonth = BuildInternSummaryFromData(selectedAccount, donationData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        dashboard.CurrentYear = BuildInternSummaryFromData(selectedAccount, donationData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        dashboard.PriorYear = BuildInternSummaryFromData(selectedAccount, donationData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

        SetYearAverages(dashboard, now);
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

    private async Task PopulateSummariesFromSessionCacheAsync(FinancialDashboardDto dashboard, UserAccountContextAccount selectedAccount, bool isDefaultAccount)
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

        var cachedData = await _sessionAccountDataCache.GetOrLoadAccountDataAsync(selectedAccount, windowStart, windowEnd, isDefaultAccount);

        var currentMonth = BuildSummaryFromCache(selectedAccount, cachedData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        var priorMonth = BuildSummaryFromCache(selectedAccount, cachedData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        var currentYear = BuildSummaryFromCache(selectedAccount, cachedData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        var priorYear = BuildSummaryFromCache(selectedAccount, cachedData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

        var balanceTasks = new[]
        {
            (Summary: currentMonth.Summary, Start: currentMonthStart, End: currentMonthEnd),
            (Summary: priorMonth.Summary, Start: priorMonthStart, End: priorMonthEnd),
            (Summary: currentYear.Summary, Start: currentYearStart, End: currentYearEnd),
            (Summary: priorYear.Summary, Start: priorYearStart, End: priorYearEnd)
        }
        .Select(async item =>
        {
            var startingBalanceTask = _accountCalculationService.CalculateBalanceAsync(
                selectedAccount,
                null,
                item.Start.AddDays(-1));
            var endingBalanceTask = _accountCalculationService.CalculateBalanceAsync(
                selectedAccount,
                null,
                item.End);
            await Task.WhenAll(startingBalanceTask, endingBalanceTask);
            item.Summary.StartingBalance = startingBalanceTask.Result.TotalBalance;
            item.Summary.Balance = endingBalanceTask.Result.TotalBalance;
        })
        .ToArray();

        await Task.WhenAll(balanceTasks);

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

    private async Task PopulateSummariesDirectAsync(FinancialDashboardDto dashboard, UserAccountContextAccount selectedAccount)
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
            priorYearStart,
            currentYearEnd);

        var currentMonthTask = BuildSummaryFromAggregatesAsync(selectedAccount, donationData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        var priorMonthTask = BuildSummaryFromAggregatesAsync(selectedAccount, donationData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        var currentYearTask = BuildSummaryFromAggregatesAsync(selectedAccount, donationData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        var priorYearTask = BuildSummaryFromAggregatesAsync(selectedAccount, donationData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

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

    private async Task<HomeSummaryResult> BuildSummaryFromAggregatesAsync(UserAccountContextAccount account, DonationCalculationData donationData, DateTime startDate, DateTime endDate, string period)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        // _logger.LogInformation("Dashboard summary period-start period={Period} account={Account} range={StartDate}..{EndDate}", period, account.Fund, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        var donationTotals = _accountCalculationService.CalculateDonationTotalsFromData(
            account,
            donationData.Donations,
            donationData.SubAccounts,
            startDate,
            endDate);
        var expenseTask = _accountCalculationService.CalculateBalanceAsync(account, startDate, endDate);
        var balanceTask = _accountCalculationService.CalculateBalanceAsync(account, null, endDate);
        var startingBalanceTask = _accountCalculationService.CalculateBalanceAsync(account, null, startDate.AddDays(-1));
        await Task.WhenAll(expenseTask, balanceTask, startingBalanceTask);

        var balanceCalculation = expenseTask.Result;
        var expenseTotal = balanceCalculation.ExpenseTotal;
        var transferTotal = balanceCalculation.TransferTotal;
        var balance = balanceTask.Result.TotalBalance;
        // _logger.LogInformation("Dashboard summary period-complete period={Period} account={Account} elapsedMs={ElapsedMs}", period, account.Fund, stopwatch.ElapsedMilliseconds);

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
                StartingBalance = startingBalanceTask.Result.TotalBalance,
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
            cachedData.AccountingData,
            account.BalanceAdjustment,
            startDate,
            endDate);

        var asOfBalance = _accountCalculationService.CalculateBalanceFromData(
            cachedData.AccountingData,
            account.BalanceAdjustment,
            cachedData.WindowStart,
            endDate);

        var startingBalance = _accountCalculationService.CalculateBalanceFromData(
            cachedData.AccountingData,
            account.BalanceAdjustment,
            cachedData.WindowStart,
            startDate.AddDays(-1));

        var expenseTotal = periodBalance.ExpenseTotal;
        summary.TotalDonations = donationTotals.TotalDonations;
        summary.PrimaryDonations = donationTotals.PrimaryDonations;
        summary.TotalOverhead = donationTotals.OverheadTotal;
        summary.SeparateSubfundTotals = new Dictionary<string, decimal>(donationTotals.SeparateSubfundTotals, StringComparer.OrdinalIgnoreCase);
        summary.TotalExpenses = expenseTotal;
        summary.InternalTransfers = periodBalance.TransferTotal;
        summary.StartingBalance = startingBalance.TotalBalance;
        summary.Balance = asOfBalance.TotalBalance;

        _logger.LogInformation(
            "Dashboard summary [{Period}] Fund={Fund} Donations={Donations} Expenses={Expenses} Transfers={Transfers} Balance={Balance} AccountingRows={AccountingRows} ExpenseRows={ExpenseRows} TransferRows={TransferRows}",
            period,
            account.Fund,
            summary.TotalDonations,
            summary.TotalExpenses,
            summary.InternalTransfers,
            summary.Balance,
            cachedData.AccountingData.Count,
            periodBalance.ExpenseTransactions.Count,
            periodBalance.TransferTransactions.Count);

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
            "Home balance account values: accountId={AccountId}, fund={Fund}, otherAccounts={OtherAccounts}",
            account.AccountId,
            account.Fund,
            string.Join("|", distinctAccounts));
    }

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