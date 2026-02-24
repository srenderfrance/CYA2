using ModelsLibrary;
using System.Collections.Generic;
using System.Linq;

namespace Cya2.Application.Utilities
{
    public static class TransactionCategorizer
    {
        public static CategorizedTransactions CategorizeTransactions(List<AccountingDataModel> transactions)
        {
            if (transactions == null)
                transactions = new List<AccountingDataModel>();

            var expenseTransactions = transactions.Where(e =>
                e.Type == "Payroll Check" ||
                e.Type == "Expense" ||
                (e.Account != null && (e.Account.Contains("Expenses:") || e.Account.Contains("Payroll:") || e.Account.Contains("Administration:")))
            ).ToList();

            var transferTransactions = transactions.Where(e =>
                e.Account != null && e.Account.Contains("Transfer")
            ).ToList();

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

        public static bool IsExpense(AccountingDataModel transaction)
        {
            if (transaction == null) return false;

            return transaction.Type == "Payroll Check" ||
                   transaction.Type == "Expense" ||
                   (transaction.Account != null && (transaction.Account.Contains("Expenses:") || transaction.Account.Contains("Payroll:") || transaction.Account.Contains("Administration:")));
        }

        public static bool IsTransfer(AccountingDataModel transaction)
        {
            if (transaction == null) return false;

            return transaction.Account != null && transaction.Account.Contains("Transfer");
        }

        public static bool ShouldSubtractFromBalance(AccountingDataModel transaction)
        {
            if (transaction == null) return false;

            return transaction.Type == "Payroll Check" ||
                   transaction.Type == "Expense" ||
                   (transaction.Account != null && (transaction.Account.Contains("Expenses:") || transaction.Account.Contains("Payroll:") || transaction.Account.Contains("Administration:")));
        }
    }

    public class CategorizedTransactions
    {
        public List<AccountingDataModel> ExpenseTransactions { get; set; } = new();
        public List<AccountingDataModel> TransferTransactions { get; set; } = new();
        public List<AccountingDataModel> OtherTransactions { get; set; } = new();

        public decimal ExpenseTotal => ExpenseTransactions.Sum(e => System.Convert.ToDecimal(e.Amount));
        public decimal TransferTotal => TransferTransactions.Sum(e => System.Convert.ToDecimal(e.Amount));
        public decimal OtherTotal => OtherTransactions.Sum(e => System.Convert.ToDecimal(e.Amount));
    }
}
