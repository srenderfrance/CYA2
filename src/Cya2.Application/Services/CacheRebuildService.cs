using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Application.Models;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class CacheRebuildService : ICacheRebuildService
{
    private readonly IAdminPreloadService _adminPreloadService;
    private readonly IFinancialDashboardService _financialDashboardService;
    private readonly IDonorService _donorService;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly ILogger<CacheRebuildService> _logger;

    public CacheRebuildService(
        IAdminPreloadService adminPreloadService,
        IFinancialDashboardService financialDashboardService,
        IDonorService donorService,
        IDonationReadRepository donationReadRepository,
        IExpenseReadRepository expenseReadRepository,
        ILogger<CacheRebuildService> logger)
    {
        _adminPreloadService = adminPreloadService;
        _financialDashboardService = financialDashboardService;
        _donorService = donorService;
        _donationReadRepository = donationReadRepository;
        _expenseReadRepository = expenseReadRepository;
        _logger = logger;
    }

    public async Task RebuildAsync(bool warmSummaries = true, CancellationToken cancellationToken = default)
    {
        var rebuildStarted = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Central cache rebuild started: summaryWarming={SummaryWarming}", warmSummaries);
        var preload = await _adminPreloadService.PreloadMetadataAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var internNames = await _donationReadRepository.GetCurrentInternNamesAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var now = DateTime.Now;
        var homeStart = new DateTime(now.Year - 1, 1, 1);
        var homeEnd = now.Date;
        var normalAccounts = preload.Accounts
            .Select(account => (account.AccountId, account.Fund, MergedSubFunds: (IReadOnlyList<string>)preload.SubAccounts
                .Where(subAccount => subAccount.AccountId == account.AccountId && string.Equals(subAccount.Kind, "Merged", StringComparison.OrdinalIgnoreCase))
                .Select(subAccount => subAccount.SubFund)
                .ToList()))
            .Where(account => !string.IsNullOrWhiteSpace(account.Fund))
            .ToList();
        var donationTask = _donationReadRepository.GetDonationsForAccountsAsync(normalAccounts, homeStart, homeEnd);
        var internDonationTask = _donationReadRepository.GetInternDonationsByNamesAndDateRangeAsync(internNames, homeStart, homeEnd);
        var accountingTask = _expenseReadRepository.GetAccountingDataForAccountsAsync(
            preload.Accounts.Select(account => (account.AccountId, account.AccountingClass, account.AccountNumber)).ToList(),
            homeStart,
            homeEnd);
        _logger.LogInformation(
            "Central cache rebuild financial loads started: normalDonationTask={NormalDonationTask}, internDonationTask={InternDonationTask}, accountingTask={AccountingTask}",
            !donationTask.IsCompleted,
            !internDonationTask.IsCompleted,
            !accountingTask.IsCompleted);
        await Task.WhenAll(donationTask, internDonationTask, accountingTask);
        _logger.LogInformation(
            "Central cache rebuild source data loaded: accounts={AccountCount}, interns={InternCount}, donationRows={DonationRowCount}, accountingRows={AccountingRowCount}, elapsedMs={ElapsedMs}",
            preload.Accounts.Count,
            internNames.Count,
            donationTask.Result.Values.Sum(rows => rows.Count) + internDonationTask.Result.Values.Sum(rows => rows.Count),
            accountingTask.Result.Values.Sum(rows => rows.Count),
            rebuildStarted.ElapsedMilliseconds);
        var data = new CacheRebuildData
        {
            Accounts = preload.Accounts,
            SubAccounts = preload.SubAccounts,
            DonationsByAccountId = donationTask.Result,
            DonationsByInternName = internDonationTask.Result,
            AccountingByAccountId = accountingTask.Result
        };
        await _adminPreloadService.RebuildOverviewFromDataAsync(data, cancellationToken);

        if (!warmSummaries)
        {
            _logger.LogInformation(
                "Central cache rebuild completed without dashboard summary warming: accounts={AccountCount}, interns={InternCount}",
                preload.Accounts.Count,
                internNames.Count);
            return;
        }

        using var throttler = new SemaphoreSlim(2, 2);
        var missingGiftRange = new Cya2.Core.ValueObjects.DateRange(now.Date.AddMonths(-6), now.Date);
        var missingGiftTasks = preload.Accounts.Select(account =>
            WarmMissingGiftDonorsAsync(account, data, missingGiftRange, throttler, cancellationToken));
        await Task.WhenAll(missingGiftTasks);

        var normalTasks = preload.Accounts.Select(account =>
            WarmNormalSummaryAsync(account, data, throttler, cancellationToken));
        var internTasks = internNames.Select(internName =>
            WarmInternSummaryAsync(internName, data, throttler, cancellationToken));

        await Task.WhenAll(normalTasks.Concat(internTasks));
        _logger.LogInformation(
            "Central cache rebuild completed: accounts={AccountCount}, interns={InternCount}, elapsedMs={ElapsedMs}",
            preload.Accounts.Count,
            internNames.Count,
            rebuildStarted.ElapsedMilliseconds);
    }

    private async Task WarmMissingGiftDonorsAsync(
        Cya2.Core.Entities.Account account,
        CacheRebuildData data,
        Cya2.Core.ValueObjects.DateRange dateRange,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        if (!data.DonationsByAccountId.TryGetValue(account.AccountId, out var donations))
        {
            donations = [];
        }

        await throttler.WaitAsync(cancellationToken);
        try
        {
            await _donorService.WarmMissingGiftDonorsFromDataAsync(
                new Cya2.Application.DTOs.AccountOptionDto
                {
                    AccountId = account.AccountId,
                    Fund = account.Fund,
                    AccountingClass = account.AccountingClass,
                    AccountNumber = account.AccountNumber,
                    Overhead = account.Overhead
                },
                donations,
                dateRange,
                cancellationToken);
        }
        finally
        {
            throttler.Release();
        }
    }

    private async Task WarmNormalSummaryAsync(
        Cya2.Core.Entities.Account account,
        CacheRebuildData data,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        await throttler.WaitAsync(cancellationToken);
        try
        {
            await _financialDashboardService.WarmHomeSummaryFromDataAsync(account, data, cancellationToken);
        }
        finally
        {
            throttler.Release();
        }
    }

    private async Task WarmInternSummaryAsync(
        string internName,
        CacheRebuildData data,
        SemaphoreSlim throttler,
        CancellationToken cancellationToken)
    {
        await throttler.WaitAsync(cancellationToken);
        try
        {
            await _financialDashboardService.WarmInternSummaryFromDataAsync(internName, data, cancellationToken);
        }
        finally
        {
            throttler.Release();
        }
    }
}
