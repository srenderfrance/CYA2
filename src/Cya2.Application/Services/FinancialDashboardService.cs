using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using DataLibrary; // Main application's IDataAccess
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelsLibrary; // Main application's models
using UtilityClassLibrary; // External utility library
using System.Security.Claims;

namespace Cya2.Application.Services;

/// <summary>
/// Clean architecture service for financial dashboard operations
/// Provides abstraction for Home.razor component
/// </summary>
public class FinancialDashboardService : IFinancialDashboardService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FinancialDashboardService> _logger;

    public FinancialDashboardService(
        IDataAccess dataAccess, 
        IConfiguration configuration, 
        ILogger<FinancialDashboardService> logger)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<FinancialDashboardDto> GetDashboardDataAsync(string accountFund, string userId)
    {
        try
        {
            _logger.LogInformation("Getting dashboard data for account {AccountFund} and user {UserId}", accountFund, userId);

            var dashboard = new FinancialDashboardDto();

            // For MVP: Load user accounts directly
            dashboard.UserAccounts = await GetUserAccountsAsync(userId);
            
            if (!dashboard.UserAccounts.Any())
            {
                _logger.LogWarning("No accounts found for user {UserId}", userId);
                return dashboard;
            }

            // Set selected account or default
            var selectedAccount = !string.IsNullOrEmpty(accountFund) 
                ? dashboard.UserAccounts.FirstOrDefault(a => a.Fund == accountFund)
                : dashboard.UserAccounts.FirstOrDefault(a => a.IsDefault) ?? dashboard.UserAccounts.First();

            if (selectedAccount == null)
            {
                _logger.LogWarning("Selected account {AccountFund} not found for user {UserId}", accountFund, userId);
                return dashboard;
            }

            dashboard.SelectedAccount = selectedAccount.Fund;
            dashboard.HasAccountData = true;

            // Calculate financial summaries using direct database access for MVP
            await CalculateFinancialSummariesAsync(dashboard, selectedAccount);

            return dashboard;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard data for account {AccountFund}", accountFund);
            return new FinancialDashboardDto();
        }
    }

    public async Task<List<UserAccountDto>> GetUserAccountsAsync(string userId)
    {
        try
        {
            const string sql = @"
                SELECT a.AccountId, a.Fund, a.AccountingClass, a.AccountNumber, a.Overhead,
                       CASE WHEN au.AccountId IS NOT NULL THEN 1 ELSE 0 END as HasAccess
                FROM Accounts a
                LEFT JOIN AccountsUsers au ON a.AccountId = au.AccountId 
                LEFT JOIN Users u ON au.UserId = u.Id
                WHERE u.Email = @UserEmail OR u.GoogleId = @UserId
                ORDER BY a.Fund";

            var accounts = await _dataAccess.LoadData<dynamic, dynamic>(
                sql, 
                new { UserEmail = userId, UserId = userId }, 
                GetConnectionString());

            var result = new List<UserAccountDto>();
            bool isFirst = true;

            foreach (var account in accounts ?? new List<dynamic>())
            {
                result.Add(new UserAccountDto
                {
                    AccountId = account.AccountId,
                    Fund = account.Fund?.ToString() ?? "",
                    DisplayName = account.Fund?.ToString() ?? "", // Simple display for MVP
                    AccountingClass = account.AccountingClass?.ToString() ?? "",
                    AccountNumber = account.AccountNumber?.ToString() ?? "",
                    Overhead = Convert.ToDecimal(account.Overhead ?? 0),
                    IsDefault = isFirst // First account is default
                });
                isFirst = false;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user accounts for {UserId}", userId);
            return new List<UserAccountDto>();
        }
    }

    public async Task<bool> ValidateAccountAccessAsync(string accountFund, string userId)
    {
        try
        {
            var userAccounts = await GetUserAccountsAsync(userId);
            return userAccounts.Any(a => a.Fund.Equals(accountFund, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating account access for {AccountFund} and user {UserId}", accountFund, userId);
            return false;
        }
    }

    private async Task CalculateFinancialSummariesAsync(FinancialDashboardDto dashboard, UserAccountDto selectedAccount)
    {
        try
        {
            var now = DateTime.Now;

            // Load donation and accounting data for the selected account
            var donationData = await LoadDonationDataAsync(selectedAccount.Fund);
            var accountingData = await LoadAccountingDataAsync(selectedAccount.Fund);

            if (!donationData.Any() && !accountingData.Any())
            {
                _logger.LogWarning("No financial data found for account {Fund}", selectedAccount.Fund);
                SetEmptyFinancialSummaries(dashboard, now);
                return;
            }

            // Calculate date ranges
            var currentMonthStart = new DateTime(now.Year, now.Month, 1);
            var currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);
            var priorMonthStart = currentMonthStart.AddMonths(-1);
            var priorMonthEnd = currentMonthStart.AddDays(-1);
            var currentYearStart = new DateTime(now.Year, 1, 1);
            var currentYearEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
            var priorYearStart = new DateTime(now.Year - 1, 1, 1);
            var priorYearEnd = new DateTime(now.Year - 1, 12, 31);

            // Calculate summaries for each period
            dashboard.CurrentMonth = await CalculatePeriodSummaryAsync(donationData, accountingData, currentMonthStart, currentMonthEnd, now.ToString("MMMM yyyy"));
            dashboard.PriorMonth = await CalculatePeriodSummaryAsync(donationData, accountingData, priorMonthStart, priorMonthEnd, now.AddMonths(-1).ToString("MMMM yyyy"));
            dashboard.CurrentYear = await CalculatePeriodSummaryAsync(donationData, accountingData, currentYearStart, currentYearEnd, now.ToString("yyyy"));
            dashboard.PriorYear = await CalculatePeriodSummaryAsync(donationData, accountingData, priorYearStart, priorYearEnd, (now.Year - 1).ToString());

            // Calculate averages for year summaries
            int monthsInCurrentYear = Math.Min(now.Month, 12);
            if (monthsInCurrentYear > 0)
            {
                dashboard.CurrentYear.AvgMonthlyDonations = dashboard.CurrentYear.TotalDonations / monthsInCurrentYear;
                dashboard.CurrentYear.AvgMonthlyExpenses = dashboard.CurrentYear.TotalExpenses / monthsInCurrentYear;
            }

            dashboard.PriorYear.AvgMonthlyDonations = dashboard.PriorYear.TotalDonations / 12;
            dashboard.PriorYear.AvgMonthlyExpenses = dashboard.PriorYear.TotalExpenses / 12;

            // Calculate balances from accounting data
            dashboard.CurrentMonth.Balance = CalculateBalance(accountingData, currentMonthEnd);
            dashboard.PriorMonth.Balance = CalculateBalance(accountingData, priorMonthEnd);
            dashboard.CurrentYear.Balance = CalculateBalance(accountingData, currentYearEnd);
            dashboard.PriorYear.Balance = CalculateBalance(accountingData, priorYearEnd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating financial summaries for account {AccountFund}", selectedAccount.Fund);
            SetEmptyFinancialSummaries(dashboard, DateTime.Now);
        }
    }

    private async Task<FinancialSummaryDto> CalculatePeriodSummaryAsync(
        List<DonationsDataModel> donationData, 
        List<AccountingDataModel> accountingData, 
        DateTime startDate, 
        DateTime endDate, 
        string period)
    {
        var summary = new FinancialSummaryDto { Period = period };

        // Calculate donations for period
        var periodDonations = donationData.Where(d => d.Date >= startDate && d.Date <= endDate);
        summary.TotalDonations = periodDonations.Sum(d => (decimal)d.Amount);

        // Calculate expenses and transfers from accounting data
        var periodAccounting = accountingData.Where(a => a.Date >= startDate && a.Date <= endDate);
        
        foreach (var transaction in periodAccounting)
        {
            var amount = (decimal)transaction.Amount;
            if (amount < 0) // Expenses are typically negative
            {
                if (IsTransfer(transaction))
                {
                    summary.InternalTransfers += Math.Abs(amount);
                }
                else if (IsOverhead(transaction))
                {
                    summary.TotalOverhead += Math.Abs(amount);
                }
                else
                {
                    summary.TotalExpenses += Math.Abs(amount);
                }
            }
        }

        return summary;
    }

    private async Task<List<DonationsDataModel>> LoadDonationDataAsync(string fund)
    {
        try
        {
            const string sql = "SELECT * FROM DonationData WHERE Fund = @Fund ORDER BY Date";
            var data = await _dataAccess.LoadData<DonationsDataModel, dynamic>(
                sql, 
                new { Fund = fund }, 
                GetConnectionString());
            return data?.ToList() ?? new List<DonationsDataModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading donation data for fund {Fund}", fund);
            return new List<DonationsDataModel>();
        }
    }

    private async Task<List<AccountingDataModel>> LoadAccountingDataAsync(string fund)
    {
        try
        {
            const string sql = "SELECT * FROM AccountingData WHERE AccountingClass = @AccountingClass ORDER BY Date";
            var data = await _dataAccess.LoadData<AccountingDataModel, dynamic>(
                sql, 
                new { AccountingClass = fund }, 
                GetConnectionString());
            return data?.ToList() ?? new List<AccountingDataModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading accounting data for fund {Fund}", fund);
            return new List<AccountingDataModel>();
        }
    }

    private decimal CalculateBalance(List<AccountingDataModel> accountingData, DateTime asOfDate)
    {
        return accountingData
            .Where(t => t.Date <= asOfDate)
            .Sum(t => (decimal)t.Amount);
    }

    private bool IsTransfer(AccountingDataModel transaction)
    {
        // Simple heuristic - check if account or type indicates transfer
        var type = transaction.Type?.ToLower() ?? "";
        var account = transaction.Account?.ToLower() ?? "";
        
        return type.Contains("transfer") || 
               account.Contains("transfer") ||
               type.Contains("internal");
    }

    private bool IsOverhead(AccountingDataModel transaction)
    {
        // Simple heuristic - check if transaction is overhead-related
        var account = transaction.Account?.ToLower() ?? "";
        
        return account.Contains("overhead") || 
               account.Contains("admin") ||
               account.Contains("management");
    }

    private void SetEmptyFinancialSummaries(FinancialDashboardDto dashboard, DateTime now)
    {
        dashboard.CurrentMonth = new FinancialSummaryDto { Period = now.ToString("MMMM yyyy") };
        dashboard.PriorMonth = new FinancialSummaryDto { Period = now.AddMonths(-1).ToString("MMMM yyyy") };
        dashboard.CurrentYear = new FinancialSummaryDto { Period = now.ToString("yyyy") };
        dashboard.PriorYear = new FinancialSummaryDto { Period = (now.Year - 1).ToString() };
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("default") ?? string.Empty;
    }
}