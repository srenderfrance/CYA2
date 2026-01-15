using DataLibrary;
using Microsoft.Extensions.Configuration;
using ModelsLibrary;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UtilityClasses
{
    /// <summary>
    /// Balance calculation utility using the logic from BalanceTest.razor
    /// Replaces the old BalanceCalculator with the correct expense categorization logic
    /// </summary>
    public static class BalanceCalculator
    {
        /// <summary>
        /// Calculates balance using direct database queries
        /// </summary>
        /// <param name="selectedAccount">The account to calculate balance for</param>
        /// <param name="dataAccess">Database access interface</param>
        /// <param name="connectionString">Database connection string</param>
        /// <param name="startDate">Start date for calculation (if null, uses all data)</param>
        /// <param name="endDate">End date for calculation</param>
        /// <returns>Balance calculation result</returns>
        public static async Task<BalanceCalculationResult> CalculateBalanceFromDatabaseAsync(
            Account selectedAccount,
            IDataAccess dataAccess,
            string connectionString,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            if (selectedAccount == null)
                throw new ArgumentNullException(nameof(selectedAccount));

            var actualStartDate = startDate ?? DateTime.MinValue;
            var actualEndDate = endDate ?? DateTime.MaxValue;

            try
            {
                // Sum base calculation using the BalanceTest.razor logic
                string sumSql = @"SELECT COALESCE(SUM(
                    CASE 
                        WHEN Type IN ('Payroll Check', 'Expense') OR Account LIKE '%Expenses:%' OR Account LIKE '%Payroll:%' OR Account LIKE '%Administration:%' THEN -Amount 
                        ELSE Amount 
                    END
                ), 0) FROM AccountingData WHERE (AccountingClass = @AccountingClass OR AccountNumber = @AccountNumber) AND Account != 'Prepaids'";

                if (startDate.HasValue)
                    sumSql += " AND Date >= @StartDate";
                if (endDate.HasValue)
                    sumSql += " AND Date <= @EndDate";

                var sumVal = await dataAccess.LoadData<decimal, dynamic>(sumSql, 
                    new { 
                        AccountingClass = selectedAccount.AccountingClass, 
                        AccountNumber = selectedAccount.AccountNumber, 
                        StartDate = actualStartDate, 
                        EndDate = actualEndDate 
                    }, connectionString);

                decimal baseSum = sumVal?.FirstOrDefault() ?? 0.00m;
                decimal calculatedBalance = baseSum + selectedAccount.BalanceAdjustment;

                // Load all matching rows for detailed breakdown
                string entriesSql = @"SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
                                     FROM AccountingData
                                     WHERE (AccountingClass = @AccountingClass OR AccountNumber = @AccountNumber) AND Account != 'Prepaids'";

                if (startDate.HasValue)
                    entriesSql += " AND Date >= @StartDate";
                if (endDate.HasValue)
                    entriesSql += " AND Date <= @EndDate";

                entriesSql += " ORDER BY Date DESC";

                var entries = await dataAccess.LoadData<AccountingDataModel, dynamic>(entriesSql, 
                    new { 
                        AccountingClass = selectedAccount.AccountingClass, 
                        AccountNumber = selectedAccount.AccountNumber, 
                        StartDate = actualStartDate, 
                        EndDate = actualEndDate 
                    }, connectionString);

                return CalculateBalanceFromData(entries?.ToList() ?? new List<AccountingDataModel>(), selectedAccount.BalanceAdjustment);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error calculating balance from database: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Calculates balance using pre-loaded data
        /// </summary>
        /// <param name="entries">Pre-loaded accounting data</param>
        /// <param name="balanceAdjustment">Balance adjustment from account settings</param>
        /// <param name="startDate">Start date for calculation (if null, uses all data)</param>
        /// <param name="endDate">End date for calculation (if null, uses all data)</param>
        /// <returns>Balance calculation result</returns>
        public static BalanceCalculationResult CalculateBalanceFromData(
            List<AccountingDataModel> entries,
            decimal balanceAdjustment = 0.00m,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            if (entries == null)
                entries = new List<AccountingDataModel>();

            // Filter by date range if provided
            if (startDate.HasValue || endDate.HasValue)
            {
                entries = entries.Where(e => 
                    (!startDate.HasValue || e.Date >= startDate.Value) &&
                    (!endDate.HasValue || e.Date <= endDate.Value)
                ).ToList();
            }

            // Categorize transactions using the BalanceTest.razor logic
            var expenseTransactions = entries.Where(e => 
                e.Type == "Payroll Check" || 
                e.Type == "Expense" || 
                (e.Account != null && (e.Account.Contains("Expenses:") || e.Account.Contains("Payroll:") || e.Account.Contains("Administration:")))
            ).ToList();

            var transferTransactions = entries.Where(e => 
                e.Account != null && e.Account.Contains("Transfer")
            ).ToList();

            var otherTransactions = entries.Where(e => 
                !expenseTransactions.Contains(e) && !transferTransactions.Contains(e)
            ).ToList();

            // Calculate totals for each category
            var expenseTotal = expenseTransactions.Sum(e => Convert.ToDecimal(e.Amount));
            var transferTotal = transferTransactions.Sum(e => Convert.ToDecimal(e.Amount));
            var otherTotal = otherTransactions.Sum(e => Convert.ToDecimal(e.Amount));

            // Calculate balance using the BalanceTest.razor logic
            var calculatedBalance = balanceAdjustment + entries.Sum(e => 
                (e.Type == "Payroll Check" || e.Type == "Expense" || 
                 (e.Account != null && (e.Account.Contains("Expenses:") || e.Account.Contains("Payroll:") || e.Account.Contains("Administration:")))) 
                ? -Convert.ToDecimal(e.Amount) : Convert.ToDecimal(e.Amount));

            return new BalanceCalculationResult
            {
                TotalBalance = calculatedBalance,
                ExpenseTotal = expenseTotal,
                TransferTotal = transferTotal,
                OtherTotal = otherTotal,
                ExpenseTransactions = expenseTransactions,
                TransferTransactions = transferTransactions,
                OtherTransactions = otherTransactions,
                AllTransactions = entries
            };
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

    /// <summary>
    /// Result of balance calculation containing totals and categorized transactions
    /// </summary>
    public class BalanceCalculationResult
    {
        public decimal TotalBalance { get; set; }
        public decimal ExpenseTotal { get; set; }
        public decimal TransferTotal { get; set; }
        public decimal OtherTotal { get; set; }
        public List<AccountingDataModel> ExpenseTransactions { get; set; } = new();
        public List<AccountingDataModel> TransferTransactions { get; set; } = new();
        public List<AccountingDataModel> OtherTransactions { get; set; } = new();
        public List<AccountingDataModel> AllTransactions { get; set; } = new();
    }
}