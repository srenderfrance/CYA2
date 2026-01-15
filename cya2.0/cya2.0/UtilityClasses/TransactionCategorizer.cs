using ModelsLibrary;
using System.Collections.Generic;
using System.Linq;

namespace UtilityClasses
{
    /// <summary>
    /// Transaction categorization utility using the logic from BalanceTest.razor
    /// Replaces the old TransactionCategorizer with the correct expense categorization logic
    /// </summary>
    public static class TransactionCategorizer
    {
        /// <summary>
        /// Categorizes a list of transactions into expense, transfer, and other categories
        /// </summary>
        /// <param name="transactions">List of accounting transactions to categorize</param>
        /// <returns>Categorized transaction result</returns>
        public static CategorizedTransactions CategorizeTransactions(List<AccountingDataModel> transactions)
        {
            if (transactions == null)
                transactions = new List<AccountingDataModel>();

            // Expense transactions - using BalanceTest.razor logic
            var expenseTransactions = transactions.Where(e => 
                e.Type == "Payroll Check" || 
                e.Type == "Expense" || 
                (e.Account != null && (e.Account.Contains("Expenses:") || e.Account.Contains("Payroll:") || e.Account.Contains("Administration:")))
            ).ToList();

            // Transfer transactions - using BalanceTest.razor logic
            var transferTransactions = transactions.Where(e => 
                e.Account != null && e.Account.Contains("Transfer")
            ).ToList();

            // Other transactions - everything else
            var otherTransactions = transactions.Where(e => 
                !expenseTransactions.Contains(e) && !transferTransactions.Contains(e)
            ).ToList();

            return new CategorizedTransactions
            {
                ExpenseTransactions = expenseTransactions,
                TransferTransactions = transferTransactions,
                OtherTransactions = otherTransactions
            };
        }

        /// <summary>
        /// Determines if a transaction is an expense based on BalanceTest.razor logic
        /// </summary>
        /// <param name="transaction">Transaction to check</param>
        /// <returns>True if the transaction is an expense</returns>
        public static bool IsExpense(AccountingDataModel transaction)
        {
            if (transaction == null) return false;

            return transaction.Type == "Payroll Check" || 
                   transaction.Type == "Expense" || 
                   (transaction.Account != null && (transaction.Account.Contains("Expenses:") || transaction.Account.Contains("Payroll:") || transaction.Account.Contains("Administration:")));
        }

        /// <summary>
        /// Determines if a transaction is a transfer based on BalanceTest.razor logic
        /// </summary>
        /// <param name="transaction">Transaction to check</param>
        /// <returns>True if the transaction is a transfer</returns>
        public static bool IsTransfer(AccountingDataModel transaction)
        {
            if (transaction == null) return false;

            return transaction.Account != null && transaction.Account.Contains("Transfer");
        }

        /// <summary>
        /// Determines if a transaction should be subtracted from balance (expense logic from BalanceTest.razor)
        /// </summary>
        /// <param name="transaction">Transaction to check</param>
        /// <returns>True if the transaction should be subtracted from balance</returns>
        public static bool ShouldSubtractFromBalance(AccountingDataModel transaction)
        {
            if (transaction == null) return false;

            return transaction.Type == "Payroll Check" || 
                   transaction.Type == "Expense" || 
                   (transaction.Account != null && (transaction.Account.Contains("Expenses:") || transaction.Account.Contains("Payroll:") || transaction.Account.Contains("Administration:")));
        }

        // Deprecated methods kept for backward compatibility
        [System.Obsolete("Use IsExpense method instead")]
        public static bool IsFundraising(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.Account))
                return false;

            return !transaction.Account.StartsWith("6") &&
                   transaction.Account.Contains("Fundraising", System.StringComparison.OrdinalIgnoreCase);
        }

        [System.Obsolete("Use IsExpense method instead")]
        public static bool IsOtherExpense(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.Account) || string.IsNullOrEmpty(transaction.Type))
                return false;

            return transaction.Account.Contains("fees", System.StringComparison.OrdinalIgnoreCase) &&
                   transaction.Type.Equals("Journal Entry", System.StringComparison.OrdinalIgnoreCase);
        }

        [System.Obsolete("Use IsTransfer method instead")]
        public static bool IsInternalTransfer(AccountingDataModel transaction)
        {
            return IsTransfer(transaction);
        }
    }

    /// <summary>
    /// Result of transaction categorization
    /// </summary>
    public class CategorizedTransactions
    {
        public List<AccountingDataModel> ExpenseTransactions { get; set; } = new();
        public List<AccountingDataModel> TransferTransactions { get; set; } = new();
        public List<AccountingDataModel> OtherTransactions { get; set; } = new();

        /// <summary>
        /// Gets the total amount for expense transactions
        /// </summary>
        public decimal ExpenseTotal => ExpenseTransactions.Sum(e => System.Convert.ToDecimal(e.Amount));

        /// <summary>
        /// Gets the total amount for transfer transactions
        /// </summary>
        public decimal TransferTotal => TransferTransactions.Sum(e => System.Convert.ToDecimal(e.Amount));

        /// <summary>
        /// Gets the total amount for other transactions
        /// </summary>
        public decimal OtherTotal => OtherTransactions.Sum(e => System.Convert.ToDecimal(e.Amount));
    }
}

