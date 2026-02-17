using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByExternalIdAsync(string externalId);
    Task<List<User>> GetActiveUsersAsync();
    Task<bool> ExistsAsync(string email);
}