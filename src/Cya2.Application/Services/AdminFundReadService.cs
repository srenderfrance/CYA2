using Cya2.Core.Entities;
using Cya2.Core.Interfaces;

namespace Cya2.Application.Services;

public class AdminFundReadService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ISubAccountRepository _subAccountRepository;

    public AdminFundReadService(IAccountRepository accountRepository, ISubAccountRepository subAccountRepository)
    {
        _accountRepository = accountRepository;
        _subAccountRepository = subAccountRepository;
    }

    public async Task<List<Account>> GetAllAccountsAsync()
    {
        return await _accountRepository.GetAllAsync();
    }

    public async Task<List<SubAccount>> GetSubAccountsAsync(int? accountId = null)
    {
        return accountId.HasValue && accountId.Value > 0
            ? await _subAccountRepository.GetByAccountIdAsync(accountId.Value)
            : await _subAccountRepository.GetAllAsync();
    }
}
