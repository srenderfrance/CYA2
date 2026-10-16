using Cya2.Application.DTOs;
using Cya2.Core.Entities;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IDonorService
{
    Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange);
    Task<List<DonorSummaryDto>> GetDonorSummariesAsync(IEnumerable<string> fundNames, DateRange dateRange);
    Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(int accountId, string accountFund, DateRange dateRange);
    Task<List<DonorSummaryDto>> GetDonorSummariesForAccountAsync(AccountOptionDto account, DateRange dateRange, string userId = "");
    Task<List<DonorSummaryDto>> GetDonorSummariesForSelectionAsync(AccountOptionDto account, string selectedSubAccount, DateRange? dateRange, string userId = "");
    Task<List<DonorSummaryDto>> GetMissingGiftDonorsAsync(AccountOptionDto account, DateRange dateRange);
    Task WarmMissingGiftDonorsFromDataAsync(AccountOptionDto account, IReadOnlyList<Cya2.Core.ReadModels.DonationRecord> donations, DateRange dateRange, CancellationToken cancellationToken = default);
    Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(int accountId, string accountFund);
    Task<List<DonorSummaryDto>> GetAllDonorSummariesAsync(IEnumerable<string> fundNames);
    Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund);
    Task<DonorDetailDto?> GetDonorDetailForAccountAsync(string donorName, AccountOptionDto account);
    Task<List<string>> GetDonorNamesAsync(string accountFund);
    Task<List<SubAccount>> GetSubAccountsForAccountAsync(int accountId);
    Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund);
    Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund);
    Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed, 
                                   string address, string city, string state, string postal, string country);

    // Debugging helper: last executed SQL and parameters (if any)
    string? GetLastQuery();
}