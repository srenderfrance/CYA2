using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLibrary;
using Microsoft.Extensions.Configuration;
using ModelsLibrary;

namespace UtilityClasses
{
    /// <summary>
    /// Calculates donation totals and overhead totals for an account over a date range,
    /// including handling of SubAccounts with Kind = "Merged" and "Separate".
    /// </summary>
    public class DonationsTotalsCalculator
    {
        private readonly IDataAccess _data;
        private readonly IConfiguration _config;

        public DonationsTotalsCalculator(IDataAccess data, IConfiguration config)
        {
            _data = data;
            _config = config;
        }

        /// <summary>
        /// Calculate donation totals and overhead for the given account and date range.
        /// </summary>
        public async Task<DonationsTotalsResult> CalculateAsync(Account account, DateTime? start = null, DateTime? end = null)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            var conn = _config.GetConnectionString("default") ?? string.Empty;
            var s = start ?? DateTime.MinValue;
            var e = end ?? DateTime.MaxValue;

            // Load primary account donations using Fund (Fund Notes)
            const string donationsSql = @"SELECT Amount, Date, Fund FROM DonationData WHERE Fund = @Fund AND Date >= @Start AND Date <= @End";
            var primaryDonations = await _data.LoadData<DonationLite, dynamic>(
                donationsSql,
                new { Fund = account.Fund, Start = s, End = e },
                conn);

            decimal primaryTotal = primaryDonations?.Sum(d => Convert.ToDecimal(d.Amount)) ?? 0m;

            // Load subaccounts for this account
            const string subSql = @"SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId";
            var subAccounts = await _data.LoadData<SubAccountLite, dynamic>(subSql, new { AccountId = account.AccountId }, conn) ?? new List<SubAccountLite>();

            // Prepare result structures
            var separateTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            decimal mergedExtrasTotal = 0m;

            foreach (var sub in subAccounts)
            {
                // For each subfund, read donations by its SubFund reference
                var subDonations = await _data.LoadData<DonationLite, dynamic>(
                    donationsSql,
                    new { Fund = sub.SubFund, Start = s, End = e },
                    conn);
                decimal subTotal = subDonations?.Sum(d => Convert.ToDecimal(d.Amount)) ?? 0m;

                if (string.Equals(sub.Kind, "Merged", StringComparison.OrdinalIgnoreCase))
                {
                    mergedExtrasTotal += subTotal;
                }
                else
                {
                    separateTotals[sub.SubFund] = subTotal;
                }
            }

            // Total donations counted for primary account view
            decimal totalDonations = primaryTotal + mergedExtrasTotal;

            // Account.Overhead is a percent (e.g., 12 => 12%)
            decimal overheadTotal = Math.Round(totalDonations * (account.Overhead / 100m), 2);

            return new DonationsTotalsResult
            {
                AccountId = account.AccountId,
                PrimaryFundRef = account.Fund,
                PrimaryFundName = account.Fund,
                Start = s,
                End = e,
                PrimaryDonations = primaryTotal,
                MergedSubfundDonations = mergedExtrasTotal,
                TotalDonations = totalDonations,
                OverheadTotal = overheadTotal,
                SeparateSubfundTotals = separateTotals
            };
        }

        private sealed class DonationLite
        {
            public DateTime Date { get; set; }
            public double Amount { get; set; }
            public string Fund { get; set; } = string.Empty;
        }

        private sealed class SubAccountLite
        {
            public int Id { get; set; }
            public int AccountId { get; set; }
            public string SubFund { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
        }
    }

    public class DonationsTotalsResult
    {
        public int AccountId { get; set; }
        public string PrimaryFundRef { get; set; } = string.Empty; // AccountingClass used to query donations
        public string PrimaryFundName { get; set; } = string.Empty; // Display fund name
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public decimal PrimaryDonations { get; set; }
        public decimal MergedSubfundDonations { get; set; }
        public decimal TotalDonations { get; set; }
        public decimal OverheadTotal { get; set; }
        public Dictionary<string, decimal> SeparateSubfundTotals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
