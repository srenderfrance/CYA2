using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface ISubAccountRepository
{
    Task<List<SubAccount>> GetAllAsync();
    Task<List<SubAccount>> GetByAccountIdAsync(int accountId);
    Task<SubAccount?> GetByIdAsync(int id);
    Task<bool> ExistsByNameAsync(int accountId, string subFund, int? excludeId = null);
    Task<SubAccount> AddAsync(SubAccount entity);
    Task<SubAccount> UpdateAsync(SubAccount entity);
    Task<bool> DeleteAsync(int id);
}
