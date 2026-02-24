using Cya2.Application.DTOs;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for financial dashboard calculations and summaries
/// Replaces direct business logic in Home.razor component
/// </summary>
public interface IFinancialDashboardService
{
    /// <summary>
    /// Get complete financial dashboard data for an account
    /// </summary>
    Task<FinancialDashboardDto> GetDashboardDataAsync(string accountFund, string userId);

    /// <summary>
    /// Get user accounts accessible to the user
    /// </summary>
    Task<List<UserAccountDto>> GetUserAccountsAsync(string userId);

    /// <summary>
    /// Validate user has access to specific account
    /// </summary>
    Task<bool> ValidateAccountAccessAsync(string accountFund, string userId);
}