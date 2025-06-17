using ModelsLibrary;
using System;

namespace UtilityClasses
{

    public static class TransactionCategorizer
    {
        public static bool IsExpense(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.account))
                return false;

            // Expense accounts start with 6
            return transaction.account.StartsWith("6");
        }

        public static bool IsFundraising(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.account))
                return false;

            // Fundraising: Account doesn't start with 6 but includes "Fundraising"
            return !transaction.account.StartsWith("6") &&
                   transaction.account.Contains("Fundraising", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOtherExpense(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.account) || string.IsNullOrEmpty(transaction.type))
                return false;

            // Other expenses: Account includes "fees" and type is "Journal Entry"
            return transaction.account.Contains("fees", StringComparison.OrdinalIgnoreCase) &&
                   transaction.type.Equals("Journal Entry", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInternalTransfer(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.account) || string.IsNullOrEmpty(transaction.type))
                return false;

            // Internal transfers: Look for "Transfer - Internal" pattern and type is "Journal Entry"
            return transaction.account.Contains("Transfer - Internal", StringComparison.OrdinalIgnoreCase) &&
                   transaction.type.Equals("Journal Entry", StringComparison.OrdinalIgnoreCase);
        }
    }
}

