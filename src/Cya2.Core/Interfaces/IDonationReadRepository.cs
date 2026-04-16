using Cya2.Core.Entities;
using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

public interface IDonationReadRepository
{
    Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId);
    Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames);
    Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName);
    Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate);
    Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate);
    Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName);
    Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName);
    Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm);
    Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm);
}
