using Microsoft.Extensions.Logging;
using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILogger<AccountService> _logger;

    public AccountService(IAccountRepository accountRepository, ILogger<AccountService> logger)
    {
        _accountRepository = accountRepository;
        _logger = logger;
    }

    public async Task<List<AccountSummaryDto>> GetUserAccountsAsync(string userId)
    {
        try
        {
            var accounts = await _accountRepository.GetByUserIdAsync(userId);

            return accounts.Select(a => new AccountSummaryDto
            {
                Fund = a.Fund,
                AccountingClass = a.AccountingClass,
                DisplayName = a.Fund,
                CurrentBalance = a.BalanceAdjustment,
                LastActivity = a.CreatedAt
            }).ToList();
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
            var account = await _accountRepository.GetByFundAsync(accountName);
            if (account == null) return null;

            return new AccountDetailDto
            {
                Fund = account.Fund,
                AccountingClass = account.AccountingClass,
                AccountNumber = account.AccountNumber,
                BalanceAdjustment = account.BalanceAdjustment,
                Overhead = account.Overhead,
                SoftCredit = account.SoftCredit
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account detail for: {AccountName}", accountName);
            return null;
        }
    }

    public Task<AccountBalanceDto> GetAccountBalanceAsync(string accountName, DateRange dateRange)
    {
        // Simplified implementation - in full app this would perform aggregation
        return Task.FromResult(new AccountBalanceDto
        {
            AccountName = accountName,
            Balance = 0m,
            TotalIncome = 0m,
            TotalExpenses = 0m,
            TotalDonations = 0m,
            AsOfDate = DateTime.Today
        });
    }

    public Task<List<AccountBalanceDto>> GetAllAccountBalancesAsync(DateRange dateRange)
    {
        return Task.FromResult(new List<AccountBalanceDto>());
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
}