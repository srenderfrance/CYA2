using Cya2.Application.DTOs;
using Cya2.Core.ValueObjects;
using ModelsLibrary;

namespace Cya2.Application.Interfaces;

/// <summary>
/// Clean expense service interface for Expenses page migration (standalone file)
/// </summary>
public interface IExpenseService
{
    Task<List<Account>> GetUserAccountsAsync(string userId, bool isAdminOrViewer = false);
    Task<ExpenseDataDto> GetExpenseDataAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false);
    Task<List<ExpenseTransactionDto>> GetExpenseTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false);
    Task<List<ExpenseTransactionDto>> GetTransferTransactionsAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false);
    Task<ExpenseSummaryDto> GetExpenseSummaryAsync(string accountName, DateRange dateRange, string userId, bool isAdminOrViewer = false);
}