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

    private readonly IAccountSnapshotCache _snapshotCache;
    private readonly IAccountSnapshotLoader _snapshotLoader;
    private readonly IFinancialDashboardService _dashboardService;
    private readonly ISessionDashboardDtoCacheService _dashboardCache;
    private readonly IDonationService _donationService;
    private readonly IExpenseService _expenseService;
    private readonly IDonorService _donorService;
    private readonly ILogger<AccountSnapshotWarmupService> _logger;
    private readonly object _sync = new();
    private readonly LinkedList<AccountSnapshotKey> _recentKeys = new();
    private readonly Dictionary<AccountSnapshotKey, Task> _warmups = new();
    private readonly SemaphoreSlim _warmupGate = new(MaxConcurrentWarmups, MaxConcurrentWarmups);
    private long _warmupSequence;
    private CancellationTokenSource _backgroundCts = new();
    private AccountSnapshotKey? _defaultKey;
    private List<UserAccountContextAccount> _initialAccounts = new();
    private string _userId = string.Empty;
    private bool _isAdminOrViewer;

    public AccountSnapshotWarmupService(
        IAccountSnapshotCache snapshotCache,
        IAccountSnapshotLoader snapshotLoader,
        IFinancialDashboardService dashboardService,
        ISessionDashboardDtoCacheService dashboardCache,
        IDonationService donationService,
        IExpenseService expenseService,
        IDonorService donorService,
        ILogger<AccountSnapshotWarmupService> logger)
    {
        _snapshotCache = snapshotCache;
        _snapshotLoader = snapshotLoader;
        _dashboardService = dashboardService;
        _dashboardCache = dashboardCache;
        _donationService = donationService;
        _expenseService = expenseService;
        _donorService = donorService;
        _logger = logger;
    }

    public void WarmDefaultAccount(Account account)
        => WarmDefaultAccount(ToContextAccount(account));

    public async Task WarmInitialAccountsAsync(IEnumerable<UserAccountContextAccount> accounts, int? defaultAccountId, string userId = "", bool isAdminOrViewer = false, DateRange? donorSummaryRange = null)
    {
        var orderedAccounts = (accounts ?? [])
            .Where(account => account.AccountId > 0 && !string.IsNullOrWhiteSpace(account.Fund))
            .ToList();
        var defaultAccount = defaultAccountId.HasValue
            ? orderedAccounts.FirstOrDefault(account => account.AccountId == defaultAccountId.Value)
            : null;

        _logger.LogInformation(
            "Account warmup schedule: user={UserId}, accounts={AccountCount}, defaultAccountId={DefaultAccountId}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
            userId,
            orderedAccounts.Count,
            defaultAccountId,
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
                await StartWarmup(defaultAccount, CancellationToken.None, donorSummaryRange);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Default account snapshot warmup failed: accountId={AccountId}, fund={Fund}", defaultAccount.AccountId, defaultAccount.Fund);
            }
        }

        var accountsToWarm = orderedAccounts
            .Where(account => defaultAccount is null || account.AccountId != defaultAccount.AccountId)
            .Take(defaultAccount is null ? MaxAccountsWithoutDefault : MaxNonDefaultAccounts)
            .ToList();

        StartBackgroundWarmup(accountsToWarm, donorSummaryRange);
    }

    public void RecordSelection(Account account)
        => RecordSelection(ToContextAccount(account));

    public void RecordSelection(UserAccountContextAccount account)
        => RecordSelection(account, _userId, _isAdminOrViewer);

    public void RecordSelection(UserAccountContextAccount account, string userId, bool isAdminOrViewer = false)
        => _ = WarmSelectedAccountAsync(account, userId, isAdminOrViewer);

    public Task WarmSelectedAccountAsync(UserAccountContextAccount account, string userId, bool isAdminOrViewer = false, DateRange? donorSummaryRange = null)
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

        // _logger.LogInformation("Prioritizing selected account snapshot: accountId={AccountId}, fund={Fund}; canceled background warmup", key.AccountId, key.Fund);

        var selectedWarmup = StartWarmup(account, CancellationToken.None, donorSummaryRange);
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

        _snapshotCache.InvalidateAll();
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
        AccountSnapshotKey selectedKey,
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
            _logger.LogWarning(ex, "Selected account snapshot warmup failed: accountId={AccountId}, fund={Fund}", selectedKey.AccountId, selectedKey.Fund);
        }

        List<UserAccountContextAccount> remaining;
        CancellationToken token;
        lock (_sync)
        {
            _backgroundCts.Dispose();
            _backgroundCts = new CancellationTokenSource();
            token = _backgroundCts.Token;
            var maxRecentAccounts = _defaultKey.HasValue ? MaxNonDefaultAccounts : MaxAccountsWithoutDefault;
            var recentKeys = _recentKeys.ToHashSet();
            remaining = _initialAccounts
                .Where(account => GetKey(account) != selectedKey)
                .Where(account => _defaultKey is null || GetKey(account) != _defaultKey.Value)
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
                        "Account warmup join: request={RequestId}, accountId={AccountId}, fund={Fund}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
                        requestId,
                        key.AccountId,
                        key.Fund,
                        donorSummaryRange?.StartDate,
                        donorSummaryRange?.EndDate);
                    return existing;
                }

                _warmups.Remove(key);
            }

            if (_snapshotCache.TryGet(key, out _))
            {
                TouchRecent(key);
                EvictRecentIfNeeded();
                snapshotWasCached = true;
            }

            _logger.LogInformation(
                "Account warmup inspect: request={RequestId}, accountId={AccountId}, fund={Fund}, snapshotCached={SnapshotCached}, warningRange={WarningStart:yyyy-MM-dd}..{WarningEnd:yyyy-MM-dd}",
                requestId,
                key.AccountId,
                key.Fund,
                snapshotWasCached,
                donorSummaryRange?.StartDate,
                donorSummaryRange?.EndDate);

            if (!snapshotWasCached)
            {
                var warmup = StartTrackedWarmup(
                    key,
                    () => WarmAsync(account, key, cancellationToken, donorSummaryRange));
                _logger.LogInformation("Account warmup started: request={RequestId}, accountId={AccountId}, fund={Fund}, mode=snapshot-and-derived", requestId, key.AccountId, key.Fund);
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
            _logger.LogInformation("Account warmup started: request={RequestId}, accountId={AccountId}, fund={Fund}, mode=derived-only", requestId, key.AccountId, key.Fund);
            return derivedWarmup;
        }
    }

    private Task StartTrackedWarmup(AccountSnapshotKey key, Func<Task> warmupFactory)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _warmups[key] = completion.Task;
        _ = ExecuteTrackedWarmupAsync(key, completion, warmupFactory);
        return completion.Task;
    }

    private async Task ExecuteTrackedWarmupAsync(
        AccountSnapshotKey key,
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

    private async Task WarmAsync(UserAccountContextAccount account, AccountSnapshotKey key, CancellationToken cancellationToken, DateRange? donorSummaryRange = null)
    {
        await _warmupGate.WaitAsync(cancellationToken);
        try
        {
            await _snapshotCache.GetOrCreateAsync(
                key,
                token => _snapshotLoader.LoadAsync(account, GetSnapshotQueryRange(), key, token),
                cancellationToken);

            lock (_sync)
            {
                TouchRecent(key);
                EvictRecentIfNeeded();
            }

            // _logger.LogInformation("Warmed account snapshot: accountId={AccountId}, fund={Fund}", key.AccountId, key.Fund);

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
        if (!_dashboardCache.TryGetDashboard(_userId, account.Fund, out _))
        {
            var dashboard = await _dashboardService.GetDashboardSummaryDataAsync(account.Fund, _userId);
            _dashboardCache.SetDashboard(_userId, account.Fund, dashboard);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _donationService.GetDonationDataAsync(account.Fund, "All", range, _userId, _isAdminOrViewer);
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Account derived warmup expense load: accountId={AccountId}, fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}", account.AccountId, account.Fund, range.StartDate, range.EndDate);
        await _expenseService.GetExpenseDataAsync(account.Fund, range, _userId, _isAdminOrViewer);
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveDonorSummaryRange = donorSummaryRange ?? range;
        await _donorService.GetDonorSummariesForAccountAsync(accountOption, effectiveDonorSummaryRange);
        cancellationToken.ThrowIfCancellationRequested();
        await _donorService.GetMissingGiftDonorsAsync(accountOption, effectiveDonorSummaryRange);
        _logger.LogInformation("Account derived warmup completed: accountId={AccountId}, fund={Fund}", account.AccountId, account.Fund);
    }

    private async Task ObserveWarmupAsync(AccountSnapshotKey key, Task warmup)
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
            _logger.LogWarning(ex, "Account snapshot warmup failed: accountId={AccountId}, fund={Fund}", key.AccountId, key.Fund);
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

    private void TouchRecent(AccountSnapshotKey key)
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
        var maxRecentAccounts = _defaultKey.HasValue ? MaxNonDefaultAccounts : MaxAccountsWithoutDefault;
        while (_recentKeys.Count > maxRecentAccounts)
        {
            var node = _recentKeys.Last;
            if (node is null)
            {
                return;
            }

            _recentKeys.Remove(node);
            _snapshotCache.Remove(node.Value);
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

    private static AccountSnapshotKey GetKey(UserAccountContextAccount account)
        => new AccountSnapshotKey(account.AccountId, account.Fund, 0).Normalize();

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
    {
        var now = DateTime.UtcNow;
        return new DateRange(
            new DateTime(now.Year - 2, 1, 1),
            new DateTime(now.Year, 12, 31));
    }
}
