using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;

namespace Cya2.Application.Interfaces;

public interface IDonorService
{
    Task<List<DonorSummaryDto>> GetDonorSummariesAsync(string accountFund, DateRange dateRange);
    Task<DonorDetailDto?> GetDonorDetailAsync(string donorName, string accountFund);
    Task<List<string>> GetDonorNamesAsync(string accountFund);
    Task<string> FormatDonorContactForCopyAsync(string donorName, string accountFund);
    Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm, string accountFund);
    Task UpdateDonorContactInfoAsync(string donorName, string email, string phoneMobile, string phoneFixed, 
                                   string address, string city, string state, string postal, string country);
}