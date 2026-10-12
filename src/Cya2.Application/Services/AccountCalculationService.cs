using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Interfaces;
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
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly ExpenseClassificationService _expenseClassificationService;
    private readonly IAccountingTransactionProcessor _accountingTransactionProcessor;
    private readonly IAccountSnapshotCache? _accountSnapshotCache;
    private readonly IAccountSnapshotLoader? _accountSnapshotLoader;
    private readonly ICacheInvalidationVersion? _cacheInvalidationVersion;
    private readonly ILogger<AccountCalculationService> _logger;

    public AccountCalculationService(
        IExpenseReadRepository expenseReadRepository,
        IDonationReadRepository donationReadRepository,
        ExpenseClassificationService expenseClassificationService,
        ILogger<AccountCalculationService>? logger = null)
        : this(
            expenseReadRepository,
            donationReadRepository,
            expenseClassificationService,
            new AccountingTransactionProcessor(expenseClassificationService),
            logger,
            null,
            null,
            null)
    {
    }

    public AccountCalculationService(
        IExpenseReadRepository expenseReadRepository,
        IDonationReadRepository donationReadRepository,
        ExpenseClassificationService expenseClassificationService,
        IAccountingTransactionProcessor accountingTransactionProcessor,
        ILogger<AccountCalculationService>? logger = null,
        IAccountSnapshotCache? accountSnapshotCache = null,
        IAccountSnapshotLoader? accountSnapshotLoader = null,
        ICacheInvalidationVersion? cacheInvalidationVersion = null)
    {
        _expenseReadRepository = expenseReadRepository;
        _donationReadRepository = donationReadRepository;
        _expenseClassificationService = expenseClassificationService;
        _accountingTransactionProcessor = accountingTransactionProcessor;
        _accountSnapshotCache = accountSnapshotCache;
        _accountSnapshotLoader = accountSnapshotLoader;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger = logger ?? NullLogger<AccountCalculationService>.Instance;
    }

    public async Task<IReadOnlyDictionary<int, BalanceCalculationResult>> CalculateBalancesAsync(
        IReadOnlyList<UserAccountContextAccount> accounts,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var actualStartDate = startDate ?? DateTime.MinValue;
        var actualEndDate = endDate ?? DateTime.MaxValue;

        if (CanUseAccountSnapshot(actualStartDate, actualEndDate) && CanUseAccountSnapshotServices())
        {
            var snapshotTasks = accounts.ToDictionary(
                account => account.AccountId,
                account => LoadAccountingDataFromSnapshotAsync(account, actualStartDate, actualEndDate));
            await Task.WhenAll(snapshotTasks.Values);

            return accounts.ToDictionary(
                account => account.AccountId,
                account => _accountingTransactionProcessor.Process(
                    account,
                    snapshotTasks[account.AccountId].Result));
        }

        var accountKeys = accounts
            .Select(account => (account.AccountId, account.AccountingClass, account.AccountNumber))
            .ToList();
        var accountingData = await _expenseReadRepository.GetAccountingDataForAccountsAsync(accountKeys, actualStartDate, actualEndDate);

        return accounts.ToDictionary(
            account => account.AccountId,
            account => _accountingTransactionProcessor.Process(
                account,
                accountingData.GetValueOrDefault(account.AccountId, new List<AccountingRecord>())));
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
            var records = CanUseAccountSnapshot(actualStartDate, actualEndDate) && CanUseAccountSnapshotServices()
                ? await LoadAccountingDataFromSnapshotAsync(account, actualStartDate, actualEndDate)
                : await _expenseReadRepository.GetAccountingDataForAccountAsync(
                    account.AccountingClass,
                    account.AccountNumber,
                    actualStartDate,
                    actualEndDate);

            return _accountingTransactionProcessor.Process(
                account,
                records);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error calculating balance from repository data: {ex.Message}", ex);
        }
    }

    private bool CanUseAccountSnapshotServices()
        => _accountSnapshotCache is not null &&
           _accountSnapshotLoader is not null &&
           _cacheInvalidationVersion is not null;

    private async Task<List<AccountingRecord>> LoadAccountingDataFromSnapshotAsync(
        UserAccountContextAccount account,
        DateTime startDate,
        DateTime endDate)
    {
        var generation = _cacheInvalidationVersion!.GetGeneration(
            CacheDataDomains.Donations | CacheDataDomains.Accounting | CacheDataDomains.AccountMetadata);
        var key = new AccountSnapshotKey(account.AccountId, account.Fund, generation).Normalize();
        var snapshot = await _accountSnapshotCache!.GetOrCreateAsync(
            key,
            cancellationToken => _accountSnapshotLoader!.LoadAsync(
                account,
                GetSnapshotQueryRange(),
                key,
                cancellationToken));

        return snapshot.Accounting
            .Where(record => record.Date.Date >= startDate.Date && record.Date.Date <= endDate.Date)
            .Select(ToAccountingRecord)
            .ToList();
    }

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

    private static AccountingRecord ToAccountingRecord(AccountingSnapshot record)
        => new()
        {
            Id = record.Id,
            AccountingClass = record.AccountingClass,
            Date = record.Date,
            Num = record.Num,
            Amount = record.Amount,
            AccountNumber = record.AccountNumber,
            Account = record.Account,
            Type = record.Type,
            DateCreated = record.DateCreated
        };

    public BalanceCalculationResult CalculateBalanceFromData(
        UserAccountContextAccount account,
        IEnumerable<AccountingRecord> entries,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return _accountingTransactionProcessor.Process(account, entries, startDate, endDate);
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
        DateTime startDate,
        DateTime endDate)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        if (_accountSnapshotCache is not null &&
            _accountSnapshotLoader is not null &&
            _cacheInvalidationVersion is not null &&
            !InternAccountUtility.IsInternFund(account.Fund) &&
            CanUseAccountSnapshot(startDate, endDate))
        {
            var generation = _cacheInvalidationVersion.GetGeneration(
                CacheDataDomains.Donations | CacheDataDomains.Accounting | CacheDataDomains.AccountMetadata);
            var key = new AccountSnapshotKey(account.AccountId, account.Fund, generation).Normalize();
            var snapshot = await _accountSnapshotCache.GetOrCreateAsync(
                key,
                cancellationToken => _accountSnapshotLoader.LoadAsync(
                    account,
                    GetSnapshotQueryRange(),
                    key,
                    cancellationToken));

            return new DonationCalculationData
            {
                Donations = snapshot.Donations.Select(ToDonationRecord).ToList(),
                SubAccounts = snapshot.SubAccounts.Select(ToSubAccount).ToList()
            };
        }

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

    private static bool CanUseAccountSnapshot(DateTime startDate, DateTime endDate)
    {
        var now = DateTime.UtcNow;
        var snapshotStart = new DateTime(now.Year - 2, 1, 1);
        var snapshotEnd = new DateTime(now.Year, 12, 31);
        return startDate.Date >= snapshotStart && endDate.Date <= snapshotEnd;
    }

    private static DateRange GetSnapshotQueryRange()
    {
        var now = DateTime.UtcNow;
        return new DateRange(
            new DateTime(now.Year - 2, 1, 1),
            new DateTime(now.Year, 12, 31));
    }

    private static DonationRecord ToDonationRecord(DonationSnapshot record)
        => new()
        {
            Id = record.Id,
            DonorId = record.DonorId,
            DonorDisplayName = record.DonorDisplayName,
            Date = record.Date,
            Frequency = record.Frequency,
            AccountName = record.AccountName,
            PaymentMethod = record.PaymentMethod,
            GiftType = record.GiftType,
            Amount = record.Amount,
            Fund = record.Fund,
            Intern = record.Intern,
            HonorMemorialName = record.HonorMemorialName,
            Addressee = record.Addressee,
            SoftCreditName = record.SoftCreditName,
            Address = record.Address,
            City = record.City,
            State = record.State,
            PostalCode = record.PostalCode,
            Country = record.Country,
            Email = record.Email,
            PhoneFixed = record.PhoneFixed,
            PhoneMobile = record.PhoneMobile,
            DateCreated = record.DateCreated,
            IsAnonymous = record.IsAnonymous
        };

    private static SubAccount ToSubAccount(SubAccountSnapshot record)
        => new()
        {
            Id = record.Id,
            AccountId = record.AccountId,
            SubFund = record.SubFund,
            Kind = record.Kind
        };

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