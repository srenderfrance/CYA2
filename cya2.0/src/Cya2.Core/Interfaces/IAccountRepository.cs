using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface IAccountRepository : IRepository<Account>
{
    Task<Account?> GetByFundCodeAsync(string fundCode);
    Task<Account?> GetByFundAsync(string fund);
    Task<List<Account>> GetByUserIdAsync(string userId);
    Task<bool> ValidateUserAccessAsync(string userId, string fund);
    Task<bool> ExistsAsync(string fundCode);
}