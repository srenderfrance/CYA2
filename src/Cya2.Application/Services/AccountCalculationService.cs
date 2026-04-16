using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;

namespace Cya2.Application.Services;

/// <summary>
/// Service for performing account balance and financial calculations
/// Moves logic from BalanceCalculator and DonationsTotalsCalculator utility classes
/// </summary>
public class AccountCalculationService : IAccountCalculationService
{
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly IDonationReadRepository _donationReadRepository;

    public AccountCalculationService(
        IExpenseReadRepository expenseReadRepository,
        IDonationReadRepository donationReadRepository)
    {
        _expenseReadRepository = expenseReadRepository;
        _donationReadRepository = donationReadRepository;
    }

    /// <summary>
    /// Calculate balance using repository reads.
    /// </summary>
    public async Task<BalanceCalculationResult> CalculateBalanceAsync(UserAccountContextAccount account, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var actualStartDate = startDate ?? DateTime.MinValue;
        var actualEndDate = endDate ?? DateTime.MaxValue;

        try
        {
            var records = await _expenseReadRepository.GetAccountingDataByClassAndDateAsync(
                account.AccountingClass,
                actualStartDate,
                actualEndDate);

            return CalculateBalanceFromData(records, account.BalanceAdjustment);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error calculating balance from repository data: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Calculate balance using pre-loaded data.
    /// </summary>
    public BalanceCalculationResult CalculateBalanceFromData(List<AccountingRecord> entries, decimal balanceAdjustment = 0.00m, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (entries == null)
            entries = new List<AccountingRecord>();

        if (startDate.HasValue || endDate.HasValue)
        {
            entries = entries.Where(e =>
                (!startDate.HasValue || e.Date >= startDate.Value) &&
                (!endDate.HasValue || e.Date <= endDate.Value)
            ).ToList();
        }

        bool IsExpense(AccountingRecord e)
        {
            var type = e.Type ?? string.Empty;
            var accountName = e.Account ?? string.Empty;
            return type.Equals("Payroll Check", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Expense", StringComparison.OrdinalIgnoreCase)
                || accountName.Contains("Expenses:", StringComparison.OrdinalIgnoreCase)
                || accountName.Contains("Payroll:", StringComparison.OrdinalIgnoreCase)
                || accountName.Contains("Administration:", StringComparison.OrdinalIgnoreCase);
        }

        bool IsTransfer(AccountingRecord e)
        {
            var accountName = e.Account ?? string.Empty;
            return accountName.Contains("Transfer", StringComparison.OrdinalIgnoreCase);
        }

        var expenseTransactions = entries.Where(IsExpense).ToList();
        var transferTransactions = entries.Where(IsTransfer).ToList();
        var otherTransactions = entries.Where(e => !expenseTransactions.Contains(e) && !transferTransactions.Contains(e)).ToList();

        var expenseTotal = expenseTransactions.Sum(e => Convert.ToDecimal(e.Amount));
        var transferTotal = transferTransactions.Sum(e => Convert.ToDecimal(e.Amount));
        var otherTotal = otherTransactions.Sum(e => Convert.ToDecimal(e.Amount));

        var calculatedBalance = balanceAdjustment + entries.Sum(e =>
            IsExpense(e)
            ? -Convert.ToDecimal(e.Amount)
            : Convert.ToDecimal(e.Amount));

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
    /// Calculate donation totals and overhead using repository reads.
    /// </summary>
    public async Task<DonationTotalsResult> CalculateDonationTotalsAsync(UserAccountContextAccount account, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var start = startDate ?? DateTime.MinValue;
        var end = endDate ?? DateTime.MaxValue;

        var subAccounts = await _donationReadRepository.GetSubAccountsByAccountIdAsync(account.AccountId) ?? new List<Cya2.Core.Entities.SubAccount>();

        var allFunds = new List<string> { account.Fund };
        allFunds.AddRange(subAccounts.Select(s => s.SubFund));

        var donations = await _donationReadRepository.GetDonationsByFundsAsync(allFunds);
        var donationsInRange = donations.Where(d => d.Date >= start && d.Date <= end).ToList();

        decimal primaryTotal = donationsInRange
            .Where(d => string.Equals(d.Fund, account.Fund, StringComparison.OrdinalIgnoreCase))
            .Sum(d => Convert.ToDecimal(d.Amount));

        var separateTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal mergedExtrasTotal = 0m;

        foreach (var sub in subAccounts)
        {
            decimal subTotal = donationsInRange
                .Where(d => string.Equals(d.Fund, sub.SubFund, StringComparison.OrdinalIgnoreCase))
                .Sum(d => Convert.ToDecimal(d.Amount));

            if (string.Equals(sub.Kind, "Merged", StringComparison.OrdinalIgnoreCase))
            {
                mergedExtrasTotal += subTotal;
            }
            else
            {
                separateTotals[sub.SubFund] = subTotal;
            }
        }

        decimal totalDonations = primaryTotal + mergedExtrasTotal;
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

    public decimal CalculateOverheadAmount(UserAccountContextAccount account, decimal donationTotal)
    {
        if (account == null) return 0m;
        return Math.Round(donationTotal * (account.Overhead / 100m), 2);
    }
}