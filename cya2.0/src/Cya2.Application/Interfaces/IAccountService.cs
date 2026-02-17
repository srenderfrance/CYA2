using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IAccountService
{
    Task<List<AccountDto>> GetUserAccountsAsync(int userId);
    Task<AccountDto?> GetAccountByFundCodeAsync(string fundCode);
    Task<List<SubAccountDto>> GetSubAccountsAsync(string accountFund);
    Task<List<SubAccountDto>> GetSeparateSubAccountsAsync(string accountFund);
    Task<decimal> CalculateAccountBalanceAsync(string accountFund, DateTime asOfDate);
    Task<AccountDto> CreateAccountAsync(string fundCode, string name, Core.Enums.AccountType type);
    Task<SubAccountDto> AddSubAccountAsync(string parentFundCode, string subFundCode, Core.Enums.SubAccountType type);
}