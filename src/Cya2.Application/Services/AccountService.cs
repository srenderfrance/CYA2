using DataLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public class AccountService : IAccountService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IDataAccess dataAccess, IConfiguration configuration, ILogger<AccountService> logger)
    {
        _dataAccess = dataAccess;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<AccountSummaryDto>> GetUserAccountsAsync(string userId)
    {
        try
        {
            const string sql = @"SELECT a.Fund, a.AccountingClass, a.BalanceAdjustment, a.CreatedAt as LastActivity
                                 FROM Accounts a
                                 INNER JOIN AccountsUsers au ON a.AccountId = au.AccountId
                                 WHERE au.UserId = @UserId
                                 ORDER BY a.Fund";

            var accounts = await _dataAccess.LoadData<dynamic, object>(sql, new { UserId = userId }, GetConnectionString());

            return accounts?.Select(a => new AccountSummaryDto
            {
                Fund = a.Fund?.ToString() ?? string.Empty,
                AccountingClass = a.AccountingClass?.ToString() ?? string.Empty,
                DisplayName = a.Fund?.ToString() ?? string.Empty,
                CurrentBalance = Convert.ToDecimal(a.BalanceAdjustment ?? 0),
                LastActivity = a.LastActivity ?? DateTime.MinValue
            }).ToList() ?? new List<AccountSummaryDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user accounts for userId: {UserId}", userId);
            return new List<AccountSummaryDto>();
        }
    }

    public async Task<AccountDetailDto?> GetAccountDetailAsync(string accountName)
    {
        try
        {
            const string sql = @"SELECT Fund, AccountingClass, AccountNumber, BalanceAdjustment, Overhead, SoftCredit
                                 FROM Accounts
                                 WHERE Fund = @AccountName";

            var accounts = await _dataAccess.LoadData<dynamic, object>(sql, new { AccountName = accountName }, GetConnectionString());
            var account = accounts?.FirstOrDefault();

            if (account == null) return null;

            return new AccountDetailDto
            {
                Fund = account.Fund?.ToString() ?? string.Empty,
                AccountingClass = account.AccountingClass?.ToString() ?? string.Empty,
                AccountNumber = account.AccountNumber?.ToString() ?? string.Empty,
                BalanceAdjustment = Convert.ToDecimal(account.BalanceAdjustment ?? 0),
                Overhead = Convert.ToDecimal(account.Overhead ?? 0),
                SoftCredit = account.SoftCredit?.ToString() ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account detail for: {AccountName}", accountName);
            return null;
        }
    }

    public async Task<AccountBalanceDto> GetAccountBalanceAsync(string accountName, DateRange dateRange)
    {
        // Simplified implementation - in full app this would perform aggregation
        return new AccountBalanceDto
        {
            AccountName = accountName,
            Balance = 0m,
            TotalIncome = 0m,
            TotalExpenses = 0m,
            TotalDonations = 0m,
            AsOfDate = DateTime.Today
        };
    }

    public async Task<List<AccountBalanceDto>> GetAllAccountBalancesAsync(DateRange dateRange)
    {
        return new List<AccountBalanceDto>();
    }

    public async Task<List<AccountSummaryDto>> GetAccountsForUserAsync(string userId)
    {
        return await GetUserAccountsAsync(userId);
    }

    public async Task<AccountBalanceDto> CalculateBalanceAsync(string accountName, DateTime asOfDate)
    {
        var dateRange = new DateRange(DateTime.MinValue, asOfDate);
        return await GetAccountBalanceAsync(accountName, dateRange);
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("default") ?? string.Empty;
    }
}