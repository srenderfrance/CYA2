using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.Enums;

namespace Cya2.Application.Services;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _accountRepository;
    private readonly IUserRepository _userRepository;

    public AccountService(IAccountRepository accountRepository, IUserRepository userRepository)
    {
        _accountRepository = accountRepository;
        _userRepository = userRepository;
    }

    public async Task<List<string>> GetAccountFundsAsync()
    {
        var accounts = await _accountRepository.GetAllAsync();
        return accounts.Select(a => a.Fund).OrderBy(f => f).ToList();
    }

    public async Task<bool> ValidateAccountAccessAsync(string accountFund, string userId)
    {
        try
        {
            var accounts = await _accountRepository.GetByUserIdAsync(userId);
            return accounts.Any(a => a.Fund == accountFund);
        }
        catch
        {
            return false;
        }
    }

    public async Task<Account?> GetAccountByFundCodeAsync(string fundCode)
    {
        return await _accountRepository.GetByFundAsync(fundCode);
    }

    public async Task<List<Account>> GetUserAccountsAsync(string userId)
    {
        return await _accountRepository.GetByUserIdAsync(userId);
    }

    public async Task<decimal> CalculateAccountBalanceAsync(string accountFund, DateTime asOfDate)
    {
        var account = await _accountRepository.GetByFundAsync(accountFund);
        return account?.CalculateBalance(asOfDate) ?? 0m;
    }

    public async Task<Account> CreateAccountAsync(string fundCode, string name, AccountType type)
    {
        var account = new Account(fundCode, name, type);
        return await _accountRepository.AddAsync(account);
    }
}