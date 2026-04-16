using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface IUserAccountAccessRepository
{
    Task<List<Account>> GetUserAccountsAsync(int userId);
    Task<Account?> GetAccountByIdAsync(int accountId);
    Task<bool> HasAccessAsync(int userId, int accountId);
    Task<bool> GrantAccessAsync(int userId, int accountId);
    Task<bool> RevokeAccessAsync(int userId, int accountId);
    Task<bool> RevokeAllAccessAsync(int userId);
    Task<int> GetUserAccountCountAsync(int userId);
    Task<bool> SetUserDefaultAccountAsync(int userId, int? accountId);
}
