using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Cya2.Core.Services;
using Cya2.Core.Utilities;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cya2.Application.Services;

/// <summary>
/// Service for performing account balance and financial calculations
/// Moves logic from BalanceCalculator and DonationsTotalsCalculator utility classes
/// </summary>
public class AccountCalculationService : IAccountCalculationService
{
    private readonly IAccountingTransactionProcessor _accountingTransactionProcessor;
    private readonly IAccountFactDataService _accountFactDataService;
    private readonly ILogger<AccountCalculationService> _logger;

    private static string EnsureUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("An authenticated user ID is required for financial data access.", nameof(userId));

        return userId.Trim();
    }

    public AccountCalculationService(
        ExpenseClassificationService expenseClassificationService,
        IAccountingTransactionProcessor accountingTransactionProcessor,
        IAccountFactDataService accountFactDataService,
        ILogger<AccountCalculationService>? logger = null)
    {
        _accountingTransactionProcessor = accountingTransactionProcessor;
        _accountFactDataService = accountFactDataService;
        _logger = logger ?? NullLogger<AccountCalculationService>.Instance;
    }

    public async Task<IReadOnlyDictionary<int, BalanceCalculationResult>> CalculateBalancesAsync(
        IReadOnlyList<UserAccountContextAccount> accounts,
        string userId,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        EnsureUserId(userId);
        var actualStartDate = startDate ?? DateTime.MinValue;
        var actualEndDate = endDate ?? DateTime.MaxValue;

        var accountingTasks = accounts.ToDictionary(
            account => account.AccountId,
            account => _accountFactDataService.GetAccountingDataAsync(account, userId, actualStartDate, actualEndDate));
        await Task.WhenAll(accountingTasks.Values);

        return accounts.ToDictionary(
            account => account.AccountId,
            account => _accountingTransactionProcessor.Process(
                account,
                accountingTasks[account.AccountId].Result));
    }

    /// <summary>
    /// Calculate balance from authoritative account facts.
    /// </summary>
    public async Task<BalanceCalculationResult> CalculateBalanceAsync(UserAccountContextAccount account, string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var actualStartDate = startDate ?? DateTime.MinValue;
        var actualEndDate = endDate ?? DateTime.MaxValue;

        EnsureUserId(userId);
        var records = await _accountFactDataService!.GetAccountingDataAsync(account, userId, actualStartDate, actualEndDate);
        return _accountingTransactionProcessor.Process(account, records);
    }

    public Task<List<AccountingRecord>> LoadAccountingDataForBalanceAsync(
        UserAccountContextAccount account,
        string userId,
        DateTime startDate,
        DateTime endDate)
        => _accountFactDataService.GetAccountingDataAsync(account, EnsureUserId(userId), startDate, endDate);

    /// <summary>
    /// Calculate balance using pre-loaded data.
    /// </summary>
    public BalanceCalculationResult CalculateBalanceFromData(List<AccountingRecord> entries, decimal balanceAdjustment = 0.00m, DateTime? startDate = null, DateTime? endDate = null)
    {
        return _accountingTransactionProcessor.ProcessCandidates(
            entries,
            balanceAdjustment,
            startDate,
            endDate);
    }

    public BalanceCalculationResult CalculateBalanceFromData(
        UserAccountContextAccount account,
        IEnumerable<AccountingRecord> entries,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return _accountingTransactionProcessor.Process(account, entries, startDate, endDate);
    }

    /// <summary>
    /// Calculate donation totals and overhead from authoritative account facts.
    /// </summary>
    public async Task<DonationTotalsResult> CalculateDonationTotalsAsync(UserAccountContextAccount account, string userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        var start = startDate ?? DateTime.MinValue;
        var end = endDate ?? DateTime.MaxValue;

        var data = await LoadDonationCalculationDataAsync(account, userId, start, end);
        _logger.LogInformation(
            "Donation calculation inputs: accountId={AccountId} donations={DonationCount} subAccounts={SubAccountCount} range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}",
            account.AccountId,
            data.Donations.Count,
            data.SubAccounts.Count,
            start,
            end);

        var result = CalculateDonationTotalsFromData(account, data.Donations, data.SubAccounts, start, end);
        _logger.LogInformation(
            "Donation calculation result: accountId={AccountId} primary={Primary} merged={Merged} total={Total}",
            account.AccountId,
            result.PrimaryDonations,
            result.MergedSubfundDonations,
            result.TotalDonations);
        return result;
    }

    public async Task<DonationCalculationData> LoadDonationCalculationDataAsync(
        UserAccountContextAccount account,
        string userId,
        DateTime startDate,
        DateTime endDate)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        EnsureUserId(userId);
        return await _accountFactDataService.GetDonationDataAsync(account, userId, startDate, endDate);
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
            .Where(d => string.Equals(d.Fund?.Trim(), account.Fund?.Trim(), StringComparison.OrdinalIgnoreCase))
            .Sum(d => Convert.ToDecimal(d.Amount));

        var separateTotals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal mergedExtrasTotal = 0m;

        foreach (var sub in accountSubAccounts)
        {
            decimal subTotal = donationRows
                .Where(d => string.Equals(d.Fund?.Trim(), sub.SubFund?.Trim(), StringComparison.OrdinalIgnoreCase))
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