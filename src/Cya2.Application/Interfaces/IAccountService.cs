using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IAccountService
{
    Task<List<AccountSummaryDto>> GetUserAccountsAsync(string userId);
    Task<AccountDetailDto?> GetAccountDetailAsync(string accountName);
    Task<AccountBalanceDto> GetAccountBalanceAsync(string accountName, DateRange dateRange);
    Task<List<AccountBalanceDto>> GetAllAccountBalancesAsync(DateRange dateRange);

    // Legacy compatibility
    Task<List<AccountSummaryDto>> GetAccountsForUserAsync(string userId);
    Task<AccountBalanceDto> CalculateBalanceAsync(string accountName, DateTime asOfDate);
}