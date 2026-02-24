using Cya2.Core.ValueObjects;
using ModelsLibrary;

namespace Cya2.Application.DTOs;

/// <summary>
/// Complete expense data for Expenses.razor page
/// </summary>
public class ExpenseDataDto
{
    public List<Account> UserAccounts { get; set; } = new();
    public string SelectedAccount { get; set; } = string.Empty;
    public List<ExpenseTransactionDto> ExpenseTransactions { get; set; } = new();
    public List<ExpenseTransactionDto> TransferTransactions { get; set; } = new();
    public decimal ExpenseTotal { get; set; }
    public decimal TransferTotal { get; set; }
    public DateTime DateRangeStart { get; set; }
    public DateTime DateRangeEnd { get; set; }
    public bool HasAccountData => !string.IsNullOrEmpty(SelectedAccount);
}

/// <summary>
/// Individual expense transaction data
/// </summary>
public class ExpenseTransactionDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Num { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Account { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string AccountingClass { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
}

/// <summary>
/// Summary of expense data for a period
/// </summary>
public class ExpenseSummaryDto
{
    public decimal TotalExpenses { get; set; }
    public decimal TotalTransfers { get; set; }
    public int ExpenseTransactionCount { get; set; }
    public int TransferTransactionCount { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string AccountName { get; set; } = string.Empty;
}