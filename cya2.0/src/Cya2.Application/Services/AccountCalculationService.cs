using Cya2.Application.Interfaces;
using Cya2.Core.Entities;
using DataLibrary;
using Microsoft.Extensions.Configuration;

namespace Cya2.Application.Services;

/// <summary>
/// Service for performing account balance and financial calculations
/// Moves logic from BalanceCalculator and DonationsTotalsCalculator utility classes
/// </summary>
public class AccountCalculationService : IAccountCalculationService
{
    private readonly IDataAccess _dataAccess;
    private readonly IConfiguration _config;

    public AccountCalculationService(IDataAccess dataAccess, IConfiguration config)
    {
        _dataAccess = dataAccess;
        _config = config;
    }

    /// <summary>
    /// Calculate balance using database queries - moved from BalanceCalculator.CalculateBalanceFromDatabaseAsync
    /// </summary>
    public async Task<BalanceCalculationResult> CalculateBalanceAsync(Account account, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var actualStartDate = startDate ?? DateTime.MinValue;
        var actualEndDate = endDate ?? DateTime.MaxValue;

        try
        {
            var connectionString = _config.GetConnectionString("default") ?? string.Empty;

            // Sum calculation using the BalanceTest.razor logic
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

            var sumVal = await _dataAccess.LoadData<decimal, dynamic>(sumSql, 
                new { 
                    AccountingClass = account.AccountingClass, 
                    AccountNumber = account.AccountNumber, 
                    StartDate = actualStartDate, 
                    EndDate = actualEndDate 
                }, connectionString);

            decimal baseSum = sumVal?.FirstOrDefault() ?? 0.00m;
            decimal calculatedBalance = baseSum + account.BalanceAdjustment;

            // Load all matching rows for detailed breakdown
            string entriesSql = @"SELECT Id, AccountingClass, Date, Num, Amount, AccountNumber, Account, Type, DateCreated
                                 FROM AccountingData
                                 WHERE (AccountingClass = @AccountingClass OR AccountNumber = @AccountNumber) AND Account != 'Prepaids'";

            if (startDate.HasValue)
                entriesSql += " AND Date >= @StartDate";
            if (endDate.HasValue)
                entriesSql += " AND Date <= @EndDate";

            entriesSql += " ORDER BY Date DESC";

            var entries = await _dataAccess.LoadData<AccountingDataModel, dynamic>(entriesSql, 
                new { 
                    AccountingClass = account.AccountingClass, 
                    AccountNumber = account.AccountNumber, 
                    StartDate = actualStartDate, 
                    EndDate = actualEndDate 
                }, connectionString);

            return CalculateBalanceFromData(entries?.ToList() ?? new List<AccountingDataModel>(), account.BalanceAdjustment);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error calculating balance from database: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Calculate balance using pre-loaded data - moved from BalanceCalculator.CalculateBalanceFromData
    /// </summary>
    public BalanceCalculationResult CalculateBalanceFromData(List<AccountingDataModel> entries, decimal balanceAdjustment = 0.00m, DateTime? startDate = null, DateTime? endDate = null)
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
    /// Calculate donation totals and overhead - moved from DonationsTotalsCalculator
    /// </summary>
    public async Task<DonationTotalsResult> CalculateDonationTotalsAsync(Account account, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (account == null) 
            throw new ArgumentNullException(nameof(account));

        var connectionString = _config.GetConnectionString("default") ?? string.Empty;
        var start = startDate ?? DateTime.MinValue;
        var end = endDate ?? DateTime.MaxValue;

        // Load primary account donations using Fund (Fund Notes)
        const string donationsSql = @"SELECT Amount, Date, Fund FROM DonationData WHERE Fund = @Fund AND Date >= @Start AND Date <= @End";
        var primaryDonations = await _dataAccess.LoadData<DonationLite, dynamic>(
            donationsSql,
            new { Fund = account.Fund, Start = start, End = end },
            connectionString);

        decimal primaryTotal = primaryDonations?.Sum(d => Convert.ToDecimal(d.Amount)) ?? 0m;

        // Load subaccounts for this account
        const string subSql = @"SELECT Id, AccountId, SubFund, Kind FROM SubAccounts WHERE AccountId = @AccountId";
        var subAccounts = await _dataAccess.LoadData<SubAccountLite, dynamic>(subSql, new { AccountId = account.AccountId }, connectionString) ?? new List<SubAccountLite>();

        // Prepare result structures
        var separateTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal mergedExtrasTotal = 0m;

        foreach (var sub in subAccounts)
        {
            // For each subfund, read donations by its SubFund reference
            var subDonations = await _dataAccess.LoadData<DonationLite, dynamic>(
                donationsSql,
                new { Fund = sub.SubFund, Start = start, End = end },
                connectionString);
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
        decimal overheadTotal = CalculateOverheadAmount(account, totalDonations);

        return new DonationTotalsResult
        {
            AccountId = account.AccountId,
            PrimaryFundRef = account.Fund,
            PrimaryFundName = account.Fund,
            Start = start,
            End = end,
            PrimaryDonations = primaryTotal,
            MergedSubfundDonations = mergedExtrasTotal,
            TotalDonations = totalDonations,
            OverheadTotal = overheadTotal,
            SeparateSubfundTotals = separateTotals
        };
    }

    /// <summary>
    /// Calculate overhead amount based on account percentage
    /// </summary>
    public decimal CalculateOverheadAmount(Account account, decimal donationTotal)
    {
        if (account == null) return 0m;
        return Math.Round(donationTotal * (account.Overhead / 100m), 2);
    }

    // Private helper classes for data loading
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