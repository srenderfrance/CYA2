using ModelsLibrary;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UtilityClasses
{
    public static class TransactionCategorizer
    {
        public static bool IsExpense(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.Account))
                return false;

            return transaction.Account.StartsWith("6");
        }

        public static bool IsFundraising(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.Account))
                return false;

            return !transaction.Account.StartsWith("6") &&
                   transaction.Account.Contains("Fundraising", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsOtherExpense(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.Account) || string.IsNullOrEmpty(transaction.Type))
                return false;

            return transaction.Account.Contains("fees", StringComparison.OrdinalIgnoreCase) &&
                   transaction.Type.Equals("Journal Entry", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsInternalTransfer(AccountingDataModel transaction)
        {
            if (transaction == null || string.IsNullOrEmpty(transaction.Account) || string.IsNullOrEmpty(transaction.Type))
                return false;

            return transaction.Account.Contains("Transfer - Internal", StringComparison.OrdinalIgnoreCase) &&
                   transaction.Type.Equals("Journal Entry", StringComparison.OrdinalIgnoreCase);
        }
    }
}

