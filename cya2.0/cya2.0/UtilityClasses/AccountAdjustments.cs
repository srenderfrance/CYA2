using System;
using System.Collections.Generic;

namespace UtilityClasses
{
    /// <summary>
    /// Temporary class to store account-specific balance adjustments until full database connection is implemented
    /// </summary>
    public static class AccountAdjustments
    {
        // Dictionary mapping account names to their adjustments
        private static readonly Dictionary<string, AccountAdjustmentData> _adjustments = new()
        {
            // Adjustment for "Renderfrance, Saul and Oliva, Soledad" account
            {
                "Renderfrance, Saul and Oliva, Soledad",
                new AccountAdjustmentData
                {
                    StartingBalance = 7812.65m,
                    MissingDonations = 9000.00m,
                    ActualOverhead = -24065.88m,
                    Pre2020ExpensesOffset = 114087.36m
                }
            }
            // Add other accounts as needed with their specific adjustments
        };

        /// <summary>
        /// Gets adjustment data for the specified account
        /// </summary>
        public static AccountAdjustmentData GetAdjustments(string accountName)
        {
            if (string.IsNullOrEmpty(accountName) || !_adjustments.ContainsKey(accountName))
            {
                // Return default values if account not found
                return new AccountAdjustmentData
                {
                    StartingBalance = 0m,
                    MissingDonations = 0m,
                    ActualOverhead = 0m,
                    Pre2020ExpensesOffset = 0m
                };
            }

            return _adjustments[accountName];
        }
    }

    /// <summary>
    /// Data structure to hold account-specific adjustment values
    /// </summary>
    public class AccountAdjustmentData
    {
        /// <summary>
        /// The initial balance as of the calculation start date
        /// </summary>
        public decimal StartingBalance { get; set; }

        /// <summary>
        /// Additional donations not captured in the donations data
        /// </summary>
        public decimal MissingDonations { get; set; }

        /// <summary>
        /// The actual overhead value to use instead of calculating it
        /// </summary>
        public decimal ActualOverhead { get; set; }

        /// <summary>
        /// The sum of expenses from before 2020 that should be excluded from calculations
        /// since they are already reflected in the starting balance
        /// </summary>
        public decimal Pre2020ExpensesOffset { get; set; }
    }
}
