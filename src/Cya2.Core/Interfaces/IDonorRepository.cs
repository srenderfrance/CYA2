using Cya2.Core.Entities;

namespace Cya2.Core.Interfaces;

public interface IDonorRepository : IRepository<Donor>
{
    Task<Donor?> GetByNameAsync(string name);
    Task<List<Donor>> GetByAccountAsync(string accountFund);
    Task<List<Donor>> GetActiveAsync(DateTime asOfDate);
    Task<List<Donor>> SearchAsync(string searchTerm);
    Task<bool> ExistsAsync(string name); // Overload for name-based existence check
}