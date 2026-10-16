using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class AccountSnapshotWarmupService : IAccountSnapshotWarmupService, IDisposable
{
    private const int MaxNonDefaultAccounts = 4;
    private const int MaxAccountsWithoutDefault = 5;
    private const int MaxConcurrentWarmups = 2;

    private readonly IDonationDataCache _donationDataCache;
    private readonly IDonationDataCacheLoader _donationDataCacheLoader;
    private readonly IAccountingDataCache _accountingDataCache;
    private readonly IAccountingDataCacheLoader _accountingDataCacheLoader;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly IFinancialDashboardService _dashboardService;
    private readonly IDonationService _donationService;
    private readonly IExpenseService _expenseService;
    private readonly IDonorService _donorService;
    private readonly ILogger<AccountSnapshotWarmupService> _logger;
    private readonly object _sync = new();
    private readonly LinkedList<string> _recentKeys = new();
    private readonly Dictionary<string, Task> _warmups = new();
    private readonly SemaphoreSlim _warmupGate = new(MaxConcurrentWarmups, MaxConcurrentWarmups);
    private long _warmupSequence;
    private CancellationTokenSource _backgroundCts = new();
    private string? _defaultKey;
    private List<UserAccountContextAccount> _initialAccounts = new();
    private string _userId = string.Empty;
    private bool _isAdminOrViewer;

    public AccountSnapshotWarmupService(
        IDonationDataCache donationDataCache,
        IDonationDataCacheLoader donationDataCacheLoader,
        IAccountingDataCache accountingDataCache,
        IAccountingDataCacheLoader accountingDataCacheLoader,
        ICacheInvalidationVersion cacheInvalidationVersion,
        IFinancialDashboardService dashboardService,
        IDonationService donationService,
        IExpenseService expenseService,
        IDonorService donorService,
        ILogger<AccountSnapshotWarmupService> logger)
    {
        _donationDataCache = donationDataCache;
        _donationDataCacheLoader = donationDataCacheLoader;
        _accountingDataCache = accountingDataCache;
        _accountingDataCacheLoader = accountingDataCacheLoader;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _dashboardService = dashboardService;
        _donationService = donationService;
        _expenseService = expenseService;
        _donorService = donorService;
        _logger = logger;
    }

    public void WarmDefaultAccount(Account account)
        => WarmDefaultAccount(ToContextAccount(account));

    public async Task WarmInitialAccountsAsync(IEnumerable<UserAccountContextAccount> accounts, int? defaultAccountId, string userId = "", bool isAdminOrViewer = false, DateRange? donorSummaryRange = null, bool isAdminUser = false, CancellationToken cancellationToken = default)
    {
        var orderedAccounts = (accounts ?? [])
            .Where(account => account.AccountId > 0 && !string.IsNullOrWhiteSpace(account.Fund))
            .ToList();
        var defaultAccount = defaultAccountId.HasValue
            ? orderedAccounts.FirstOrDefault(account => account.AccountId == defaultAccountId.Value)
            : null;

        _logger.LogInformation(
            "Account warmup schedule: user={UserId}, accounts={AccountCount}, defaultAccountId={DefaultAccountId}, isAdminUser={IsAdminUser}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
            userId,
            orderedAccounts.Count,
            defaultAccountId,
            isAdminUser,
            donorSummaryRange?.StartDate,
            donorSummaryRange?.EndDate);

        lock (_sync)
        {
            _initialAccounts = orderedAccounts;
            _defaultKey = defaultAccount is null ? null : GetKey(defaultAccount);
            _userId = userId ?? string.Empty;
            _isAdminOrViewer = isAdminOrViewer;
            _backgroundCts.Cancel();
            _backgroundCts.Dispose();
            _backgroundCts = new CancellationTokenSource();
        }

        if (defaultAccount is not null)
        {
            try
            {
                await StartWarmup(defaultAccount, cancellationToken, donorSummaryRange);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Default account snapshot warmup failed: accountId={AccountId}", defaultAccount.AccountId);
            }
        }

        var accountsToWarm = isAdminUser
            ? []
            : orderedAccounts
                .Where(account => defaultAccount is null || account.AccountId != defaultAccount.AccountId)
                .Take(defaultAccount is null ? MaxAccountsWithoutDefault : MaxNonDefaultAccounts)
                .ToList();

        _logger.LogInformation(
            "Account warmup non-default schedule: user={UserId}, isAdminUser={IsAdminUser}, queuedAccounts={QueuedAccountCount}",
            userId,
            isAdminUser,
            accountsToWarm.Count);

        cancellationToken.ThrowIfCancellationRequested();
        StartBackgroundWarmup(accountsToWarm, donorSummaryRange);
    }

    public void RecordSelection(Account account)
        => RecordSelection(ToContextAccount(account));

    public void RecordSelection(UserAccountContextAccount account)
        => RecordSelection(account, _userId, _isAdminOrViewer);

    public void RecordSelection(UserAccountContextAccount account, string userId, bool isAdminOrViewer = false)
        => _ = WarmSelectedAccountAsync(account, userId, isAdminOrViewer);

    public Task WarmSelectedAccountAsync(UserAccountContextAccount account, string userId, bool isAdminOrViewer = false, DateRange? donorSummaryRange = null, CancellationToken cancellationToken = default)
    {
        if (!IsValid(account))
        {
            return Task.CompletedTask;
        }

        var key = GetKey(account);
        lock (_sync)
        {
            _userId = userId ?? string.Empty;
            _isAdminOrViewer = isAdminOrViewer;
            if (_defaultKey != key)
            {
                TouchRecent(key);
            }

        }

        // _logger.LogInformation("Prioritizing selected account cache: key={Key}", key);

        var selectedWarmup = StartWarmup(account, cancellationToken, donorSummaryRange);
        _ = ResumeBackgroundWarmupAfterSelectionAsync(selectedWarmup, key, donorSummaryRange);
        return selectedWarmup;
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _recentKeys.Clear();
            _defaultKey = null;
            _initialAccounts.Clear();
            _userId = string.Empty;
            _backgroundCts.Cancel();
            _backgroundCts.Dispose();
            _backgroundCts = new CancellationTokenSource();
        }

        _donationDataCache.InvalidateAll();
        _accountingDataCache.InvalidateAll();
    }

    private void WarmDefaultAccount(UserAccountContextAccount account)
    {
        if (!IsValid(account))
        {
            return;
        }

        lock (_sync)
        {
            _defaultKey = GetKey(account);
        }

        StartWarmup(account, CancellationToken.None);
    }

    private void StartBackgroundWarmup(IEnumerable<UserAccountContextAccount> accounts, DateRange? donorSummaryRange = null)
    {
        CancellationToken token;
        lock (_sync)
        {
            token = _backgroundCts.Token;
        }

        _ = ObserveBackgroundWarmupAsync(accounts, token, donorSummaryRange);
    }

    private async Task ObserveBackgroundWarmupAsync(
        IEnumerable<UserAccountContextAccount> accounts,
        CancellationToken cancellationToken,
        DateRange? donorSummaryRange = null)
    {
        try
        {
            foreach (var account in accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await StartWarmup(account, cancellationToken, donorSummaryRange);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // _logger.LogInformation("Account snapshot background warmup canceled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Account snapshot background warmup failed");
        }
    }

    private async Task ResumeBackgroundWarmupAfterSelectionAsync(
        Task selectedWarmup,
        string selectedKey,
        DateRange? donorSummaryRange)
    {
        try
        {
            await selectedWarmup;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Selected account cache warmup failed: key={Key}", selectedKey);
        }

        List<UserAccountContextAccount> remaining;
        CancellationToken token;
        lock (_sync)
        {
            _backgroundCts.Dispose();
            _backgroundCts = new CancellationTokenSource();
            token = _backgroundCts.Token;
            var maxRecentAccounts = _defaultKey is not null ? MaxNonDefaultAccounts : MaxAccountsWithoutDefault;
            var recentKeys = _recentKeys.ToHashSet();
            remaining = _initialAccounts
                .Where(account => GetKey(account) != selectedKey)
                .Where(account => _defaultKey is null || GetKey(account) != _defaultKey)
                .Where(account => !recentKeys.Contains(GetKey(account)))
                .Take(Math.Max(0, maxRecentAccounts - _recentKeys.Count))
                .ToList();
        }

        // _logger.LogInformation("Resuming account snapshot background warmup: queuedAccounts={QueuedAccounts}", remaining.Count);
        _ = ObserveBackgroundWarmupAsync(remaining, token, donorSummaryRange);
    }

    private Task StartWarmup(UserAccountContextAccount account, CancellationToken cancellationToken, DateRange? donorSummaryRange = null)
    {
        var key = GetKey(account);
        var requestId = Interlocked.Increment(ref _warmupSequence);
        var snapshotWasCached = false;
        lock (_sync)
        {
            if (_warmups.TryGetValue(key, out var existing))
            {
                if (!existing.IsCompleted)
                {
                    _logger.LogInformation(
                        "Account warmup join: request={RequestId}, accountId={AccountId}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
                        requestId,
                        account.AccountId,
                        donorSummaryRange?.StartDate,
                        donorSummaryRange?.EndDate);
                    return existing;
                }

                _warmups.Remove(key);
            }

            if (HasAuthoritativeCache(account))
            {
                TouchRecent(key);
                EvictRecentIfNeeded();
                snapshotWasCached = true;
            }

            _logger.LogInformation(
                "Account warmup inspect: request={RequestId}, accountId={AccountId}, snapshotCached={SnapshotCached}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
                requestId,
                account.AccountId,
                snapshotWasCached,
                donorSummaryRange?.StartDate,
                donorSummaryRange?.EndDate);

            if (!snapshotWasCached)
            {
                var warmup = StartTrackedWarmup(
                    key,
                    () => WarmAsync(account, key, cancellationToken, donorSummaryRange));
                _logger.LogInformation("Account warmup started: request={RequestId}, accountId={AccountId}, mode=authoritative-and-derived", requestId, account.AccountId);
                return warmup;
            }
        }

        if (string.IsNullOrWhiteSpace(_userId))
        {
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            if (_warmups.TryGetValue(key, out var existingDerived) && !existingDerived.IsCompleted)
            {
                return existingDerived;
            }

            var derivedWarmup = StartTrackedWarmup(
                key,
                () => WarmDerivedCachesAsync(account, cancellationToken, donorSummaryRange));
            _logger.LogInformation("Account warmup started: request={RequestId}, accountId={AccountId}, mode=derived-only", requestId, account.AccountId);
            return derivedWarmup;
        }
    }

    private Task StartTrackedWarmup(string key, Func<Task> warmupFactory)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _warmups[key] = completion.Task;
        _ = ExecuteTrackedWarmupAsync(key, completion, warmupFactory);
        return completion.Task;
    }

    private async Task ExecuteTrackedWarmupAsync(
        string key,
        TaskCompletionSource<object?> completion,
        Func<Task> warmupFactory)
    {
        try
        {
            await warmupFactory();
            completion.TrySetResult(null);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        await ObserveWarmupAsync(key, completion.Task);
    }

    private async Task WarmAsync(UserAccountContextAccount account, string key, CancellationToken cancellationToken, DateRange? donorSummaryRange = null)
    {
        await _warmupGate.WaitAsync(cancellationToken);
        try
        {
            await WarmAuthoritativeCachesAsync(account, cancellationToken);

            lock (_sync)
            {
                TouchRecent(key);
                EvictRecentIfNeeded();
            }

            // _logger.LogInformation("Warmed authoritative account caches: key={Key}", key);

            if (!string.IsNullOrWhiteSpace(_userId))
            {
                await WarmDerivedCachesAsync(account, cancellationToken, donorSummaryRange);
            }
        }
        finally
        {
            _warmupGate.Release();
        }
    }

    private async Task WarmAuthoritativeCachesAsync(UserAccountContextAccount account, CancellationToken cancellationToken)
    {
        var range = GetSnapshotQueryRange();
        var donationKey = new DonationDataCacheKey(account.AccountId, _cacheInvalidationVersion.Donations, range.StartDate, range.EndDate).Normalize();
        await _donationDataCache.GetOrCreateAsync(
            donationKey,
            token => _donationDataCacheLoader.LoadAsync(account, range, donationKey, token),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(account.InternName))
        {
            var accountingKey = new AccountingDataCacheKey(account.AccountId, account.AccountingClass, account.AccountNumber, _cacheInvalidationVersion.Accounting, range.StartDate, range.EndDate).Normalize();
            await _accountingDataCache.GetOrCreateAsync(
                accountingKey,
                token => _accountingDataCacheLoader.LoadAsync(account, range, accountingKey, token),
                cancellationToken);
        }
    }

    private async Task WarmDerivedCachesAsync(UserAccountContextAccount account, CancellationToken cancellationToken, DateRange? donorSummaryRange = null)
    {
        var range = GetSnapshotQueryRange();
        _logger.LogInformation(
            "Account derived warmup started: accountId={AccountId}, fund={Fund}, snapshotRange={SnapshotStart:yyyy-MM-dd}..{SnapshotEnd:yyyy-MM-dd}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
            account.AccountId,
            account.Fund,
            range.StartDate,
            range.EndDate,
            donorSummaryRange?.StartDate,
            donorSummaryRange?.EndDate);
        var accountOption = new Cya2.Application.DTOs.AccountOptionDto
        {
            AccountId = account.AccountId,
            Fund = account.Fund,
            AccountingClass = account.AccountingClass,
            AccountNumber = account.AccountNumber,
            Overhead = account.Overhead
        };

        cancellationToken.ThrowIfCancellationRequested();
        await _dashboardService.GetDashboardSummaryDataAsync(account.Fund, _userId);

        cancellationToken.ThrowIfCancellationRequested();
        await _donationService.GetDonationDataAsync(account.Fund, "All", range, _userId, _isAdminOrViewer);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Account derived warmup expense load: accountId={AccountId}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}", account.AccountId, range.StartDate, range.EndDate);
        await _expenseService.GetExpenseDataAsync(account.Fund, range, _userId, _isAdminOrViewer);
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveDonorSummaryRange = donorSummaryRange ?? range;
        await _donorService.GetDonorSummariesForAccountAsync(accountOption, effectiveDonorSummaryRange);
        cancellationToken.ThrowIfCancellationRequested();
        await _donorService.GetMissingGiftDonorsAsync(accountOption, effectiveDonorSummaryRange);
        _logger.LogInformation("Account derived warmup completed: accountId={AccountId}", account.AccountId);
    }

    private async Task ObserveWarmupAsync(string key, Task warmup)
    {
        try
        {
            await warmup;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Account authoritative cache warmup failed: key={Key}", key);
        }
        finally
        {
            lock (_sync)
            {
                if (_warmups.TryGetValue(key, out var current) && ReferenceEquals(current, warmup))
                {
                    _warmups.Remove(key);
                }
            }
        }
    }

    private void TouchRecent(string key)
    {
        if (_defaultKey == key)
        {
            return;
        }

        var existing = _recentKeys.Find(key);
        if (existing is not null)
        {
            _recentKeys.Remove(existing);
        }

        _recentKeys.AddFirst(key);
    }

    private void EvictRecentIfNeeded()
    {
        var maxRecentAccounts = _defaultKey is not null ? MaxNonDefaultAccounts : MaxAccountsWithoutDefault;
        while (_recentKeys.Count > maxRecentAccounts)
        {
            var node = _recentKeys.Last;
            if (node is null)
            {
                return;
            }

            _recentKeys.Remove(node);
            RemoveAuthoritativeCaches(node.Value);
                // _logger.LogInformation("Evicted non-default account snapshot: accountId={AccountId}, fund={Fund}", node.Value.AccountId, node.Value.Fund);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _backgroundCts.Cancel();
            _backgroundCts.Dispose();
            _backgroundCts = new CancellationTokenSource();
            _warmups.Clear();
            _recentKeys.Clear();
            _initialAccounts.Clear();
        }

    }

    private static bool IsValid(UserAccountContextAccount account)
        => account.AccountId > 0 && !string.IsNullOrWhiteSpace(account.Fund);

    private static string GetKey(UserAccountContextAccount account)
        => $"{account.AccountId}:{account.Fund.Trim()}";

    private bool HasAuthoritativeCache(UserAccountContextAccount account)
    {
        var range = GetSnapshotQueryRange();
        var donationKey = new DonationDataCacheKey(account.AccountId, _cacheInvalidationVersion.Donations, range.StartDate, range.EndDate).Normalize();
        if (!_donationDataCache.TryGet(donationKey, out _)) return false;
        if (Cya2.Core.Utilities.InternAccountUtility.IsInternFund(account.Fund)) return true;
        var accountingKey = new AccountingDataCacheKey(account.AccountId, account.AccountingClass, account.AccountNumber, _cacheInvalidationVersion.Accounting, range.StartDate, range.EndDate).Normalize();
        return _accountingDataCache.TryGet(accountingKey, out _);
    }

    private void RemoveAuthoritativeCaches(string key)
    {
        var separator = key.IndexOf(':');
        if (separator <= 0) return;
        if (!int.TryParse(key[..separator], out var accountId)) return;
        _donationDataCache.RemoveAccount(accountId);
        _accountingDataCache.RemoveAccount(accountId);
    }

    private static UserAccountContextAccount ToContextAccount(Account account)
        => new()
        {
            AccountId = account.AccountId,
            Fund = account.Fund ?? string.Empty,
            AccountingClass = account.AccountingClass ?? string.Empty,
            AccountNumber = account.AccountNumber ?? string.Empty,
            CreatedAt = account.CreatedAt,
            Overhead = account.Overhead,
            SoftCredit = account.SoftCredit ?? string.Empty,
            BalanceAdjustment = account.BalanceAdjustment,
            OtherFunds = account.OtherFunds
        };

    private static DateRange GetSnapshotQueryRange()
        => AccountFactCacheWindow.GetStandardWindow(DateTime.UtcNow);
}
