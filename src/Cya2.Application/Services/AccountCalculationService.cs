using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Core.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cya2.Application.Services;

/// <summary>
/// Service for performing account balance and financial calculations
/// Moves logic from BalanceCalculator and DonationsTotalsCalculator utility classes
/// </summary>
public class AccountCalculationService : IAccountCalculationService
{
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly ExpenseClassificationService _expenseClassificationService;
    private readonly ILogger<AccountCalculationService> _logger;

    public AccountCalculationService(
        IExpenseReadRepository expenseReadRepository,
        IDonationReadRepository donationReadRepository,
        ExpenseClassificationService expenseClassificationService,
        ILogger<AccountCalculationService>? logger = null)
    {
        _expenseReadRepository = expenseReadRepository;
        _donationReadRepository = donationReadRepository;
        _expenseClassificationService = expenseClassificationService;
        _logger = logger ?? NullLogger<AccountCalculationService>.Instance;
    }

    public async Task<IReadOnlyDictionary<int, BalanceCalculationResult>> CalculateBalancesAsync(
        IReadOnlyList<UserAccountContextAccount> accounts,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var actualStartDate = startDate ?? DateTime.MinValue;
        var actualEndDate = endDate ?? DateTime.MaxValue;
        var accountKeys = accounts
            .Select(account => (account.AccountId, account.AccountingClass, account.AccountNumber))
            .ToList();
        var accountingData = await _expenseReadRepository.GetAccountingDataForAccountsAsync(accountKeys, actualStartDate, actualEndDate);

        return accounts.ToDictionary(
            account => account.AccountId,
            account => CalculateBalanceFromData(
                accountingData.GetValueOrDefault(account.AccountId, new List<AccountingRecord>()),
                account.BalanceAdjustment));
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
            var records = await _expenseReadRepository.GetAccountingDataForAccountAsync(
                account.AccountingClass,
                account.AccountNumber,
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

        entries = entries
            .Where(e => !_expenseClassificationService.IsExcludedFromBalance(e))
            .ToList();

        var categorized = _expenseClassificationService.Categorize(entries);
        var calculatedBalance = balanceAdjustment + entries.Sum(e =>
            _expenseClassificationService.ShouldSubtractFromBalance(e)
                ? -Convert.ToDecimal(e.Amount)
                : Convert.ToDecimal(e.Amount));

        return new BalanceCalculationResult
        {
            TotalBalance = calculatedBalance,
            ExpenseTotal = categorized.ExpenseTotal,
            TransferTotal = categorized.TransferTotal,
            OtherTotal = categorized.OtherTotal,
            ExpenseTransactions = categorized.ExpenseTransactions,
            TransferTransactions = categorized.TransferTransactions,
            OtherTransactions = categorized.OtherTransactions,
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

        var data = await LoadDonationCalculationDataAsync(account, start, end);
        return CalculateDonationTotalsFromData(account, data.Donations, data.SubAccounts, start, end);
    }

    public async Task<DonationCalculationData> LoadDonationCalculationDataAsync(
        UserAccountContextAccount account,
        DateTime startDate,
        DateTime endDate)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var subAccounts = await _donationReadRepository.GetSubAccountsByAccountIdAsync(account.AccountId)
            ?? new List<SubAccount>();

        List<DonationRecord> donations;
        if (InternAccountUtility.IsInternFund(account.Fund) &&
            InternAccountUtility.TryGetInternDesignationName(account.Fund, out var internDesignationName))
        {
            donations = await _donationReadRepository.GetInternDonationsByDesignationAndDateRangeAsync(
                internDesignationName,
                startDate,
                endDate) ?? new List<DonationRecord>();
        }
        else
        {
            donations = await _donationReadRepository.GetDonationsForAccountTotalAsync(
                account.AccountId,
                account.Fund,
                startDate,
                endDate) ?? new List<DonationRecord>();
        }

        return new DonationCalculationData
        {
            Donations = donations,
            SubAccounts = subAccounts
        };
    }

    public DonationTotalsResult CalculateDonationTotalsFromData(
        UserAccountContextAccount account,
        IEnumerable<DonationRecord> donations,
        IEnumerable<SubAccount> subAccounts,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var start = startDate ?? DateTime.MinValue;
        var end = endDate ?? DateTime.MaxValue;
        var donationRows = (donations ?? Enumerable.Empty<DonationRecord>())
            .Where(d => d.Date.Date >= start.Date && d.Date.Date <= end.Date)
            .ToList();
        var accountSubAccounts = (subAccounts ?? Enumerable.Empty<SubAccount>()).ToList();

        // _logger.LogInformation(
        //     "Donation total inputs: accountId={AccountId}, fund={Fund}, donationRows={DonationRows}, subAccounts={SubAccounts}, merged={Merged}, separate={Separate}, mergedFunds={MergedFunds}, separateFunds={SeparateFunds}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}",
        //     account.AccountId,
        //     account.Fund,
        //     donationRows.Count,
        //     accountSubAccounts.Count,
        //     accountSubAccounts.Count(s => string.Equals(s.Kind?.Trim(), "Merged", StringComparison.OrdinalIgnoreCase)),
        //     accountSubAccounts.Count(s => string.Equals(s.Kind?.Trim(), "Separate", StringComparison.OrdinalIgnoreCase)),
        //     string.Join("|", accountSubAccounts.Where(s => string.Equals(s.Kind?.Trim(), "Merged", StringComparison.OrdinalIgnoreCase)).Select(s => s.SubFund)),
        //     string.Join("|", accountSubAccounts.Where(s => string.Equals(s.Kind?.Trim(), "Separate", StringComparison.OrdinalIgnoreCase)).Select(s => s.SubFund)),
        //     start,
        //     end);

        if (InternAccountUtility.IsInternFund(account.Fund))
        {
            var internTotal = donationRows.Sum(d => Convert.ToDecimal(d.Amount));
            return CreateDonationTotalsResult(
                account,
                start,
                end,
                internTotal,
                0m,
                new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase));
        }

        decimal primaryTotal = donationRows
            .Where(d => string.Equals(d.Fund, account.Fund, StringComparison.OrdinalIgnoreCase))
            .Sum(d => Convert.ToDecimal(d.Amount));

        var separateTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal mergedExtrasTotal = 0m;

        foreach (var sub in accountSubAccounts)
        {
            decimal subTotal = donationRows
                .Where(d => string.Equals(d.Fund, sub.SubFund, StringComparison.OrdinalIgnoreCase))
                .Sum(d => Convert.ToDecimal(d.Amount));

            if (string.Equals(sub.Kind?.Trim(), "Merged", StringComparison.OrdinalIgnoreCase))
            {
                mergedExtrasTotal += subTotal;
            }
            else
            {
                separateTotals[sub.SubFund] = subTotal;
            }
        }

        var result = CreateDonationTotalsResult(account, start, end, primaryTotal, mergedExtrasTotal, separateTotals);
        // _logger.LogInformation(
        //     "Donation total result: accountId={AccountId}, fund={Fund}, primary={Primary}, merged={Merged}, total={Total}, separateCount={SeparateCount}",
        //     account.AccountId,
        //     account.Fund,
        //     result.PrimaryDonations,
        //     result.MergedSubfundDonations,
        //     result.TotalDonations,
        //     result.SeparateSubfundTotals.Count);
        return result;
    }

    private DonationTotalsResult CreateDonationTotalsResult(
        UserAccountContextAccount account,
        DateTime start,
        DateTime end,
        decimal primaryTotal,
        decimal mergedExtrasTotal,
        Dictionary<string, decimal> separateTotals)
    {
        decimal totalDonations = primaryTotal + mergedExtrasTotal;

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
            OverheadTotal = CalculateOverheadAmount(account, totalDonations),
            SeparateSubfundTotals = separateTotals
        };
    }

    public decimal CalculateOverheadAmount(UserAccountContextAccount account, decimal donationTotal)
    {
        if (account == null) return 0m;
        return ToCoreAccount(account).CalculateOverheadAmount(donationTotal);
    }

    private static Account ToCoreAccount(UserAccountContextAccount account)
    {
        return new Account
        {
            AccountId = account.AccountId,
            Fund = account.Fund,
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            CreatedAt = account.CreatedAt,
            Overhead = account.Overhead,
            SoftCredit = account.SoftCredit,
            BalanceAdjustment = account.BalanceAdjustment,
            OtherFunds = account.OtherFunds
        };
    }
}