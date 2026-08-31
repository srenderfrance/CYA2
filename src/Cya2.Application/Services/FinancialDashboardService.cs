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
    private readonly IFinancialDashboardReadRepository _financialDashboardReadRepository;
    private readonly IDonationService _donationService;

    public FinancialDashboardService(
        ILogger<FinancialDashboardService> logger,
        IAccountCalculationService accountCalculationService,
        ISessionAccountDataCacheService sessionAccountDataCache,
        IUserAccountContextService userAccountContextService,
        IFinancialDashboardReadRepository financialDashboardReadRepository,
        IDonationService donationService)
    {
        _logger = logger;
        _accountCalculationService = accountCalculationService;
        _sessionAccountDataCache = sessionAccountDataCache;
        _userAccountContextService = userAccountContextService;
        _financialDashboardReadRepository = financialDashboardReadRepository;
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

                    var donationTotal = await _financialDashboardReadRepository.GetInternDonationTotalAsync(internDesignationName, monthStart, monthEnd);
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

            var repositoryAccount = ToCoreAccount(selectedAccount);
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

                var donationTask = _financialDashboardReadRepository.GetDonationTotalAsync(repositoryAccount, monthStart, monthEnd);
                var expenseTask = _financialDashboardReadRepository.GetExpenseTotalAsync(repositoryAccount, monthStart, monthEnd);
                var balanceTask = _financialDashboardReadRepository.GetBalanceAsOfAsync(repositoryAccount, monthEnd);

                await Task.WhenAll(donationTask, expenseTask, balanceTask);

                points.Add(new MonthlyAccountVisualizationDto
                {
                    MonthStart = monthStart,
                    MonthLabel = monthStart.ToString(singleYear ? "MMM" : "MMM yy"),
                    DonationTotal = donationTask.Result,
                    OverheadTotal = _accountCalculationService.CalculateOverheadAmount(selectedAccount, donationTask.Result),
                    ExpenseTotal = expenseTask.Result,
                    Balance = balanceTask.Result
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

        dashboard.CurrentMonth = await BuildInternSummaryFromAggregatesAsync(selectedAccount, internDesignationName, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        dashboard.PriorMonth = await BuildInternSummaryFromAggregatesAsync(selectedAccount, internDesignationName, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        dashboard.CurrentYear = await BuildInternSummaryFromAggregatesAsync(selectedAccount, internDesignationName, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        dashboard.PriorYear = await BuildInternSummaryFromAggregatesAsync(selectedAccount, internDesignationName, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

        SetYearAverages(dashboard, now);
    }

    private async Task<FinancialSummaryDto> BuildInternSummaryFromAggregatesAsync(UserAccountContextAccount account, string internDesignationName, DateTime startDate, DateTime endDate, string period)
    {
        var donationTotal = await _financialDashboardReadRepository.GetInternDonationTotalAsync(internDesignationName, startDate, endDate);

        return new FinancialSummaryDto
        {
            Period = period,
            TotalDonations = donationTotal,
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

        dashboard.CurrentMonth = BuildSummaryFromCache(selectedAccount, cachedData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        dashboard.PriorMonth = BuildSummaryFromCache(selectedAccount, cachedData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        dashboard.CurrentYear = BuildSummaryFromCache(selectedAccount, cachedData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        dashboard.PriorYear = BuildSummaryFromCache(selectedAccount, cachedData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

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

        var currentMonthTask = BuildSummaryFromAggregatesAsync(selectedAccount, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
        var priorMonthTask = BuildSummaryFromAggregatesAsync(selectedAccount, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
        var currentYearTask = BuildSummaryFromAggregatesAsync(selectedAccount, currentYearStart, currentYearEnd, now.ToString("yyyy"));
        var priorYearTask = BuildSummaryFromAggregatesAsync(selectedAccount, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

        await Task.WhenAll(currentMonthTask, priorMonthTask, currentYearTask, priorYearTask);

        dashboard.CurrentMonth = await currentMonthTask;
        dashboard.PriorMonth = await priorMonthTask;
        dashboard.CurrentYear = await currentYearTask;
        dashboard.PriorYear = await priorYearTask;

        SetYearAverages(dashboard, now);
    }

    private async Task<FinancialSummaryDto> BuildSummaryFromAggregatesAsync(UserAccountContextAccount account, DateTime startDate, DateTime endDate, string period)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var repositoryAccount = ToCoreAccount(account);
        // _logger.LogInformation("Dashboard summary period-start period={Period} account={Account} range={StartDate}..{EndDate}", period, account.Fund, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        var donationTask = _financialDashboardReadRepository.GetDonationTotalAsync(repositoryAccount, startDate, endDate);
        var expenseTask = _financialDashboardReadRepository.GetExpenseTotalAsync(repositoryAccount, startDate, endDate);
        var transferTask = _financialDashboardReadRepository.GetTransferTotalAsync(repositoryAccount, startDate, endDate);
        var balanceTask = _financialDashboardReadRepository.GetBalanceAsOfAsync(repositoryAccount, endDate);
        await Task.WhenAll(donationTask, expenseTask, transferTask, balanceTask);

        var donationTotal = donationTask.Result;
        var expenseTotal = expenseTask.Result;
        var transferTotal = transferTask.Result;
        var balance = balanceTask.Result;
        // _logger.LogInformation("Dashboard summary period-complete period={Period} account={Account} elapsedMs={ElapsedMs}", period, account.Fund, stopwatch.ElapsedMilliseconds);

        return new FinancialSummaryDto
        {
            Period = period,
            TotalDonations = donationTotal,
            TotalOverhead = _accountCalculationService.CalculateOverheadAmount(account, donationTotal),
            TotalExpenses = expenseTotal,
            InternalTransfers = transferTotal,
            Balance = balance
        };
    }

    private static Cya2.Core.Entities.Account ToCoreAccount(UserAccountContextAccount account)
    {
        return new Cya2.Core.Entities.Account
        {
            AccountId = account.AccountId,
            Fund = account.Fund ?? string.Empty,
            AccountingClass = account.AccountingClass ?? string.Empty,
            AccountNumber = account.AccountNumber ?? string.Empty,
            CreatedAt = account.CreatedAt,
            Overhead = Convert.ToDecimal(account.Overhead),
            SoftCredit = account.SoftCredit ?? string.Empty,
            BalanceAdjustment = account.BalanceAdjustment,
            OtherFunds = account.OtherFunds
        };
    }

    private FinancialSummaryDto BuildSummaryFromCache(UserAccountContextAccount account, DashboardAccountCacheData cachedData, DateTime startDate, DateTime endDate, string period)
    {
        var summary = new FinancialSummaryDto { Period = period };

        var periodDonations = cachedData.DonationData
            .Where(d => d.Date >= startDate && d.Date <= endDate)
            .Sum(d => Convert.ToDecimal(d.Amount));

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

        var expenseTotalAbs = periodBalance.ExpenseTransactions.Sum(e => Math.Abs(Convert.ToDecimal(e.Amount)));
        var transferTotalAbs = periodBalance.TransferTransactions.Sum(e => Math.Abs(Convert.ToDecimal(e.Amount)));

        summary.TotalDonations = periodDonations;
        summary.TotalOverhead = _accountCalculationService.CalculateOverheadAmount(account, periodDonations);
        summary.TotalExpenses = expenseTotalAbs;
        summary.InternalTransfers = transferTotalAbs;
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

        return summary;
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