using Cya2.Core.Entities;
using Cya2.Core.ReadModels;

namespace Cya2.Core.Interfaces;

public interface IDonationReadRepository
{
    Task<List<SubAccount>> GetSubAccountsByAccountIdAsync(int accountId);
    Task<List<DonationRecord>> GetDonationsByFundsAsync(IEnumerable<string> fundNames);
    Task<List<DonationRecord>> GetDonationsByAccountAsync(int accountId, string fundName);
    Task<List<DonationRecord>> GetDonationsByFundsAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate);
    Task<List<DonationRecord>> GetDonationsByFundNamesAndDateRangeAsync(IEnumerable<string> fundNames, DateTime startDate, DateTime endDate);
    Task<List<DonationRecord>> GetDonationsByAccountAndDateRangeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate);
    Task<List<DonationRecord>> GetDonationsForAccountTotalAsync(int accountId, string fundName, DateTime startDate, DateTime endDate);
    Task<List<DonationRecord>> GetDonationsForAccountScopeAsync(int accountId, string fundName, DateTime startDate, DateTime endDate)
        => GetDonationsForAccountTotalAsync(accountId, fundName, startDate, endDate);
    Task<List<DonationRecord>> GetDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string donorName);
    Task<List<DonationRecord>> GetDonationsByAccountAndDonorAsync(int accountId, string fundName, string donorName);
    Task<List<DonationRecord>> SearchDonationsByFundsAndDonorAsync(IEnumerable<string> fundNames, string searchTerm);
    Task<List<DonationRecord>> SearchDonationsByAccountAndDonorAsync(int accountId, string fundName, string searchTerm);
    Task<List<DonationRecord>> GetInternDonationsByInternNameAndDateRangeAsync(string internName, DateTime startDate, DateTime endDate);
    Task<IReadOnlyDictionary<int, List<DonationRecord>>> GetDonationsForAccountsAsync(
        IReadOnlyList<(int AccountId, string Fund, IReadOnlyList<string> MergedSubFunds)> accounts,
        DateTime startDate,
        DateTime endDate) =>
        Task.FromResult<IReadOnlyDictionary<int, List<DonationRecord>>>(
            new Dictionary<int, List<DonationRecord>>());
    Task<IReadOnlyDictionary<string, List<DonationRecord>>> GetInternDonationsByNamesAndDateRangeAsync(
        IReadOnlyList<string> internNames,
        DateTime startDate,
        DateTime endDate) =>
        Task.FromResult<IReadOnlyDictionary<string, List<DonationRecord>>>(
            new Dictionary<string, List<DonationRecord>>(StringComparer.OrdinalIgnoreCase));
    Task<List<string>> GetCurrentInternNamesAsync() =>
        Task.FromResult(new List<string>());
    Task<Dictionary<string, string>> GetCurrentInternNamesByFundsAsync(IEnumerable<string> funds) =>
        Task.FromResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
