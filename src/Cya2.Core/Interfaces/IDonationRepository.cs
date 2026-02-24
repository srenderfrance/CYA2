using Cya2.Core.Entities;
using Cya2.Core.ValueObjects;

namespace Cya2.Core.Interfaces;

public interface IDonationRepository : IRepository<Donation>
{
    Task<List<Donation>> GetByDonorNameAsync(string donorName);
    Task<List<Donation>> GetByAccountFundAsync(string accountFund);
    Task<List<Donation>> GetByDateRangeAsync(DateRange dateRange);
    Task<List<Donation>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<List<Donation>> GetByDonorAndAccountAsync(string donorName, string accountFund);
    Task<List<Donation>> GetByDonorAsync(string donorName);
    Task<decimal> GetTotalByAccountAsync(string accountFund, DateRange dateRange);
    Task<decimal> GetTotalByAccountFundAsync(string accountFund, DateTime startDate, DateTime endDate);
    Task<List<Donation>> GetRecentDonationsAsync(int days = 30);
}