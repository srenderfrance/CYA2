using Cya2.Application.DTOs;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;

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
    /// Get summary-only dashboard data for prefetch scenarios without persisting raw account rows in session cache.
    /// </summary>
    Task<FinancialDashboardDto> GetDashboardSummaryDataAsync(string accountFund, string userId);

    Task WarmHomeSummaryFromDataAsync(Account account, CacheRebuildData data, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task WarmInternSummaryFromDataAsync(string internName, CacheRebuildData data, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Get one financial summary for a caller-selected date range.
    /// </summary>
    Task<FinancialSummaryDto> GetCustomSummaryAsync(string accountFund, DateTime startDate, DateTime endDate, string userId);

    /// <summary>
    /// Get user accounts accessible to the user
    /// </summary>
    Task<List<UserAccountDto>> GetUserAccountsAsync(string userId);

    /// <summary>
    /// Validate user has access to specific account
    /// </summary>
    Task<bool> ValidateAccountAccessAsync(string accountFund, string userId);

    /// <summary>
    /// Get monthly account visualization values for a date range.
    /// </summary>
    Task<List<MonthlyAccountVisualizationDto>> GetMonthlyVisualizationAsync(string accountFund, DateTime startDate, DateTime endDate, string userId);
}

public sealed class DashboardAccountCacheData
{
    public string Fund { get; set; } = string.Empty;
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public List<AccountingRecord> AccountingData { get; set; } = new();
    public List<DonationRecord> DonationData { get; set; } = new();
    public List<SubAccount> SubAccounts { get; set; } = new();
    public long ApproximateBytes { get; set; }
}