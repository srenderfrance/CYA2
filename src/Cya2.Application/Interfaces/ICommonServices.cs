using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;
using ModelsLibrary;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Account management service interface for admin operations
/// </summary>
public interface IAccountService
{
    Task<List<AccountSummaryDto>> GetUserAccountsAsync(string userId);
    Task<AccountDetailDto?> GetAccountDetailAsync(string accountName);
    Task<AccountBalanceDto> GetAccountBalanceAsync(string accountName, DateRange dateRange);
    Task<List<AccountBalanceDto>> GetAllAccountBalancesAsync(DateRange dateRange);
}

/// <summary>
/// Clean donor service interface  
/// </summary>
public interface IDonorService
{
    Task<List<DonorSummaryDto>> GetDonorsAsync(string accountName, DateRange dateRange);
    Task<DonorDetailDto?> GetDonorDetailAsync(string donorName);
    Task<List<DonorSummaryDto>> SearchDonorsAsync(string searchTerm);
}

/// <summary>
/// Clean donation service interface
/// </summary>
public interface IDonationService  
{
    Task<List<DonationSummaryDto>> GetDonationsAsync(string accountName, DateRange dateRange);
    Task<DonationDetailDto?> GetDonationDetailAsync(int donationId);
    Task<decimal> GetTotalDonationsAsync(string accountName, DateRange dateRange);
}

/// <summary>
/// Legacy service interfaces to avoid conflicts (prefixed with Clean)
/// </summary>
public interface ICleanAccountService
{
    Task<List<AccountSummaryDto>> GetAccountsForUserAsync(string userId);
    Task<AccountBalanceDto> CalculateBalanceAsync(string accountName, DateTime asOfDate);
}

public interface ICleanDonorService
{
    Task<List<DonorSummaryDto>> GetDonorsByAccountAsync(string accountName, DateTime startDate, DateTime endDate);
    Task<DonorContactDto?> GetDonorContactInfoAsync(string donorName);
}

public interface ICleanDonationService
{
    Task<List<DonationSummaryDto>> GetDonationsForPeriodAsync(string accountName, DateTime startDate, DateTime endDate);
    Task<DonationStatsDto> GetDonationStatisticsAsync(string accountName, DateTime startDate, DateTime endDate);
}