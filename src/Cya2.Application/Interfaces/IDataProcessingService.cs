using Cya2.Core.Entities;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Service for data processing operations that can run both server-side and client-side
/// </summary>
public interface IDataProcessingService
{
    /// <summary>
    /// Process data with date range filtering - can run client-side when data is already loaded
    /// </summary>
    Task<List<T>> FilterByDateRangeAsync<T>(List<T> data, DateTime startDate, DateTime endDate) where T : class;
    
    /// <summary>
    /// Apply sub-account filtering logic - client-side compatible
    /// </summary>
    Task<List<Donation>> FilterBySubAccountAsync(List<Donation> donations, Account account, string? subAccountSelection = null);
    
    /// <summary>
    /// Process date range presets - pure client-side logic
    /// </summary>
    DateRangePresetResult ApplyDateRangePreset(DateRangePreset preset, DateTime? customStart = null);
    
    /// <summary>
    /// Transaction categorization logic - client-side compatible
    /// </summary>
    TransactionCategoryResult CategorizeTransactions(List<AccountingDataModel> transactions);
}

/// <summary>
/// Date range preset enumeration
/// </summary>
public enum DateRangePreset
{
    CurrentMonth,
    PreviousMonth,
    CurrentYear,
    PreviousYear,
    YearToDate,
    Custom
}

/// <summary>
/// Result of date range preset application
/// </summary>
public class DateRangePresetResult
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string DisplayText { get; set; } = string.Empty;
}

/// <summary>
/// Result of transaction categorization
/// </summary>
public class TransactionCategoryResult
{
    public List<AccountingDataModel> ExpenseTransactions { get; set; } = new();
    public List<AccountingDataModel> TransferTransactions { get; set; } = new();
    public List<AccountingDataModel> OtherTransactions { get; set; } = new();
    public decimal ExpenseTotal { get; set; }
    public decimal TransferTotal { get; set; }
    public decimal OtherTotal { get; set; }
}