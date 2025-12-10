using System;
using System.Collections.Generic;
using System.Linq;
using ModelsLibrary;

namespace UtilityClasses
{
    public static class BalanceCalculator
    {
        /// <summary>
        /// Calculates the account balance as of a given date using donations and accounting transactions.
        /// Mirrors the logic from Home.razor with parameterized adjustments.
        /// </summary>
        /// <param name="asOfDate">Date to compute balance through (inclusive).</param>
        /// <param name="donations">Donation entries for the account (date, amount, fund).</param>
        /// <param name="accountingData">QuickBooks/accounting entries for the account.</param>
        /// <param name="startingBalance">Starting balance prior to the cutoff date.</param>
        /// <param name="missingDonationsAdjustment">Optional positive adjustment added to donations.</param>
        /// <param name="overheadAdjustment">Optional overhead adjustment (can be negative).</param>
        /// <param name="cutoffDate">Only transactions on/after this date are included in calculations beyond startingBalance.</param>
        /// <returns>Calculated balance as of the given date.</returns>
        public static decimal CalculateBalanceAsOfDate(
            DateTime asOfDate,
            IEnumerable<DonationDataLite> donations,
            IEnumerable<AccountingDataModel> accountingData,
            decimal startingBalance,
            decimal missingDonationsAdjustment = 0m,
            decimal overheadAdjustment = 0m,
            DateTime? cutoffDate = null)
        {
            var cutoff = cutoffDate ?? new DateTime(2020, 1, 1);

            // If the target date is before the cutoff, return starting balance as-is.
            if (asOfDate < cutoff)
            {
                return startingBalance;
            }

            // Donations (as-of date, on/after cutoff) + optional missing donations adjustment.
            decimal totalDonations = donations?
                .Where(d => d.Date >= cutoff && d.Date <= asOfDate)
                .Sum(d => d.Amount) ?? 0m;

            totalDonations += missingDonationsAdjustment;

            // Regular expenses (excluding fundraising) as absolute sums.
            decimal totalExpenses = accountingData?
                .Where(a => a.date >= cutoff && a.date <= asOfDate
                            && TransactionCategorizer.IsExpense(a)
                            && !TransactionCategorizer.IsFundraising(a))
                .Sum(a => Math.Abs((decimal)a.amount)) ?? 0m;

            // Other expenses (fees, etc.), excluding fundraising.
            decimal totalOtherExpenses = accountingData?
                .Where(a => a.date >= cutoff && a.date <= asOfDate
                            && TransactionCategorizer.IsOtherExpense(a)
                            && !TransactionCategorizer.IsFundraising(a))
                .Sum(a => Math.Abs((decimal)a.amount)) ?? 0m;

            // Internal transfers; exclude those marked as OtherExpense to avoid double counting.
            decimal totalInternalTransfers = accountingData?
                .Where(a => a.date >= cutoff && a.date <= asOfDate
                            && TransactionCategorizer.IsInternalTransfer(a)
                            && !TransactionCategorizer.IsOtherExpense(a))
                .Sum(a => Math.Abs((decimal)a.amount)) ?? 0m;

            // Final balance = starting + donations - expenses + other-expenses + internal-transfers + overhead-adjustment
            decimal calculatedBalance = startingBalance
                                        + totalDonations
                                        - totalExpenses
                                        + totalOtherExpenses
                                        + totalInternalTransfers
                                        + overheadAdjustment;

            return calculatedBalance;
        }

        /// <summary>
        /// Calculates net balance from accounting entries only (no donations list),
        /// summing signed amounts and excluding fundraising rows. Useful when QuickBooks data
        /// already contains both income and expense entries.
        /// </summary>
        public static decimal CalculateNetBalanceFromAccounting(
            IEnumerable<AccountingDataModel> accountingData,
            DateTime? cutoffDate = null,
            DateTime? asOfDate = null)
        {
            var cutoff = cutoffDate ?? DateTime.MinValue;
            var end = asOfDate ?? DateTime.MaxValue;

            if (accountingData == null)
            {
                return 0m;
            }

            // Sum signed amounts for all non-fundraising transactions in range.
            // QuickBooks amounts should carry their sign; fundraising excluded by categorizer.
            var net = accountingData
                .Where(a => a.date >= cutoff && a.date <= end
                            && !TransactionCategorizer.IsFundraising(a))
                .Sum(a => Convert.ToDecimal(a.amount));

            return net;
        }

        /// <summary>
        /// Lightweight donation data model to keep calculator independent from page-specific types.
        /// </summary>
        public sealed class DonationDataLite
        {
            public DateTime Date { get; set; }
            public decimal Amount { get; set; }
            public string Fund { get; set; } = string.Empty;
        }
    }
}