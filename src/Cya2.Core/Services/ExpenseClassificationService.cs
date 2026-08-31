using Cya2.Core.ReadModels;

namespace Cya2.Core.Services;

/// <summary>
/// Domain service that classifies accounting transactions as expenses, transfers, or other.
/// Contains the authoritative business rules for transaction categorization.
/// No infrastructure or application framework dependencies.
/// </summary>
public class ExpenseClassificationService
{
    /// <summary>
    /// Classifies a list of accounting transactions into expense, transfer, and other categories.
    /// </summary>
    public CategorizedTransactions Categorize(List<AccountingRecord> transactions)
    {
        if (transactions == null || transactions.Count == 0)
        {
            return new CategorizedTransactions();
        }

        var expenses = transactions.Where(IsExpense).ToList();
        var transfers = transactions.Where(t => !IsExpense(t) && IsTransfer(t)).ToList();
        var other = transactions.Where(t => !IsExpense(t) && !IsTransfer(t)).ToList();

        return new CategorizedTransactions
        {
            ExpenseTransactions = expenses,
            TransferTransactions = transfers,
            OtherTransactions = other
        };
    }

    /// <summary>
    /// Returns true when the transaction is classified as an expense.
    /// </summary>
    public bool IsExpense(AccountingRecord transaction)
    {
        if (transaction == null) return false;

        return string.Equals(transaction.Type, "Payroll Check", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(transaction.Type, "Expense", StringComparison.OrdinalIgnoreCase) ||
               (transaction.Account != null &&
                   (transaction.Account.Contains("Expenses:", StringComparison.OrdinalIgnoreCase) ||
                    transaction.Account.Contains("Payroll:", StringComparison.OrdinalIgnoreCase) ||
                    transaction.Account.Contains("Administration:", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Returns true when the transaction is classified as a transfer.
    /// </summary>
    public bool IsTransfer(AccountingRecord transaction)
    {
        if (transaction == null) return false;

        return transaction.Account != null && transaction.Account.Contains("Transfer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the transaction should subtract from account balance.
    /// </summary>
    public bool ShouldSubtractFromBalance(AccountingRecord transaction)
    {
        return IsExpense(transaction);
    }
}

/// <summary>
/// Result of transaction categorization.
/// </summary>
public class CategorizedTransactions
{
    public List<AccountingRecord> ExpenseTransactions { get; set; } = new();
    public List<AccountingRecord> TransferTransactions { get; set; } = new();
    public List<AccountingRecord> OtherTransactions { get; set; } = new();

    public decimal ExpenseTotal => ExpenseTransactions.Sum(e => Convert.ToDecimal(e.Amount));
    public decimal TransferTotal => TransferTransactions.Sum(e => Convert.ToDecimal(e.Amount));
    public decimal OtherTotal => OtherTransactions.Sum(e => Convert.ToDecimal(e.Amount));
}
