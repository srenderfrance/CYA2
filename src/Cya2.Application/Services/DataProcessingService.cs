using Cya2.Application.Interfaces;
using Cya2.Core.Entities;

namespace Cya2.Application.Services;

/// <summary>
/// Data processing service that supports both server-side and client-side execution
/// Exposes static methods for client-side use and instance methods for DI scenarios
/// </summary>
public class DataProcessingService : IDataProcessingService
{
    #region Instance Methods (for Dependency Injection)
    
    public Task<List<T>> FilterByDateRangeAsync<T>(List<T> data, DateTime startDate, DateTime endDate) where T : class
    {
        return Task.FromResult(FilterByDateRange(data, startDate, endDate));
    }
    
    public Task<List<Donation>> FilterBySubAccountAsync(List<Donation> donations, Account account, string? subAccountSelection = null)
    {
        return Task.FromResult(FilterBySubAccount(donations, account, subAccountSelection));
    }
    
    public DateRangePresetResult ApplyDateRangePreset(DateRangePreset preset, DateTime? customStart = null)
    {
        return ApplyPreset(preset, customStart);
    }
    
    public TransactionCategoryResult CategorizeTransactions(List<AccountingDataModel> transactions)
    {
        return CategorizeTransactionsStatic(transactions);
    }
    
    #endregion

    #region Static Methods (for Client-Side Use)
    
    /// <summary>
    /// Static method for client-side date filtering
    /// </summary>
    public static List<T> FilterByDateRange<T>(List<T> data, DateTime startDate, DateTime endDate) where T : class
    {
        if (data == null) return new List<T>();
        
        return data.Where(item =>
        {
            // Use reflection to find Date property - handles various entity types
            var dateProperty = typeof(T).GetProperty("Date");
            if (dateProperty?.GetValue(item) is DateTime date)
            {
                return date.Date >= startDate.Date && date.Date <= endDate.Date;
            }
            return true; // Include items without date property
        }).ToList();
    }
    
    /// <summary>
    /// Static method for client-side sub-account filtering
    /// </summary>
    public static List<Donation> FilterBySubAccount(List<Donation> donations, Account account, string? subAccountSelection = null)
    {
        if (donations == null || !donations.Any() || account == null)
            return new List<Donation>();
        
        var filtered = new List<Donation>();
        
        foreach (var donation in donations)
        {
            bool shouldInclude = false;
            
            // Basic fund matching - can be enhanced with more complex SubAccount logic
            if (string.IsNullOrWhiteSpace(subAccountSelection) || subAccountSelection == "All")
            {
                // Include all donations for this account's fund
                if (string.Equals(donation.Fund, account.Fund, StringComparison.OrdinalIgnoreCase))
                {
                    shouldInclude = true;
                }
            }
            else
            {
                // Include only donations matching the specific sub-account selection
                if (string.Equals(donation.Fund, subAccountSelection, StringComparison.OrdinalIgnoreCase))
                {
                    shouldInclude = true;
                }
            }
            
            if (shouldInclude)
            {
                // Apply anonymity protection
                if (donation.IsAnonymous)
                {
                    donation.EnsureAnonymityProtection();
                }
                
                filtered.Add(donation);
            }
        }
        
        return filtered.OrderByDescending(d => d.Date).ToList();
    }
    
    /// <summary>
    /// Static method for client-side date range preset application
    /// </summary>
    public static DateRangePresetResult ApplyPreset(DateRangePreset preset, DateTime? customStart = null)
    {
        var now = DateTime.Now;
        var startOfThisMonth = new DateTime(now.Year, now.Month, 1);
        var endOfThisMonth = startOfThisMonth.AddMonths(1).AddDays(-1);
        var startOfThisYear = new DateTime(now.Year, 1, 1);
        var endOfThisYear = new DateTime(now.Year, 12, 31);
        
        return preset switch
        {
            DateRangePreset.CurrentMonth => new DateRangePresetResult
            {
                StartDate = startOfThisMonth,
                EndDate = endOfThisMonth,
                DisplayText = "Current Month"
            },
            DateRangePreset.PreviousMonth => new DateRangePresetResult
            {
                StartDate = startOfThisMonth.AddMonths(-1),
                EndDate = startOfThisMonth.AddDays(-1),
                DisplayText = "Previous Month"
            },
            DateRangePreset.CurrentYear => new DateRangePresetResult
            {
                StartDate = startOfThisYear,
                EndDate = endOfThisYear,
                DisplayText = "Current Year"
            },
            DateRangePreset.PreviousYear => new DateRangePresetResult
            {
                StartDate = new DateTime(now.Year - 1, 1, 1),
                EndDate = new DateTime(now.Year - 1, 12, 31),
                DisplayText = "Previous Year"
            },
            DateRangePreset.YearToDate => new DateRangePresetResult
            {
                StartDate = startOfThisYear,
                EndDate = now.Date,
                DisplayText = "Year to Date"
            },
            DateRangePreset.Custom => new DateRangePresetResult
            {
                StartDate = customStart ?? startOfThisMonth,
                EndDate = now.Date,
                DisplayText = "Custom Range"
            },
            _ => new DateRangePresetResult
            {
                StartDate = startOfThisMonth,
                EndDate = endOfThisMonth,
                DisplayText = "Current Month"
            }
        };
    }
    
    /// <summary>
    /// Static method for client-side transaction categorization
    /// Moves logic from TransactionCategorizer utility class
    /// </summary>
    public static TransactionCategoryResult CategorizeTransactionsStatic(List<AccountingDataModel> transactions)
    {
        if (transactions == null)
            transactions = new List<AccountingDataModel>();
        
        var expenseTransactions = transactions.Where(e =>
            e.Type == "Payroll Check" ||
            e.Type == "Expense" ||
            (e.Account != null && (e.Account.Contains("Expenses:") || 
                                   e.Account.Contains("Payroll:") || 
                                   e.Account.Contains("Administration:")))
        ).ToList();
        
        var transferTransactions = transactions.Where(e =>
            e.Account != null && e.Account.Contains("Transfer")
        ).ToList();
        
        var otherTransactions = transactions.Where(e =>
            !expenseTransactions.Contains(e) && !transferTransactions.Contains(e)
        ).ToList();
        
        return new TransactionCategoryResult
        {
            ExpenseTransactions = expenseTransactions,
            TransferTransactions = transferTransactions,
            OtherTransactions = otherTransactions,
            ExpenseTotal = expenseTransactions.Sum(e => Convert.ToDecimal(e.Amount)),
            TransferTotal = transferTransactions.Sum(e => Convert.ToDecimal(e.Amount)),
            OtherTotal = otherTransactions.Sum(e => Convert.ToDecimal(e.Amount))
        };
    }
    
    #endregion
}

/// <summary>
/// Client-side calculation helpers that can be called directly from Blazor components
/// </summary>
public static class ClientCalculations
{
    /// <summary>
    /// Quick donation total calculation for client-side use
    /// </summary>
    public static decimal CalculateDonationTotal(List<Donation> donations, DateTime? startDate = null, DateTime? endDate = null)
    {
        var filtered = startDate.HasValue || endDate.HasValue 
            ? DataProcessingService.FilterByDateRange(donations, startDate ?? DateTime.MinValue, endDate ?? DateTime.MaxValue)
            : donations;
            
        return filtered.Sum(d => (decimal)d.Amount);
    }
    
    /// <summary>
    /// Quick overhead calculation for client-side use
    /// </summary>
    public static decimal CalculateOverhead(decimal donationTotal, decimal overheadPercentage)
    {
        return Math.Round(donationTotal * (overheadPercentage / 100m), 2);
    }
    
    /// <summary>
    /// Quick balance calculation for client-side use when data is already loaded
    /// </summary>
    public static decimal CalculateQuickBalance(List<AccountingDataModel> transactions, decimal balanceAdjustment)
    {
        var categorized = DataProcessingService.CategorizeTransactionsStatic(transactions);
        return balanceAdjustment + categorized.OtherTotal - categorized.ExpenseTotal + categorized.TransferTotal;
    }
}