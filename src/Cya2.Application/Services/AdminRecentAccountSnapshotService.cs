using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.Interfaces;
using Cya2.Core.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public sealed class AdminRecentAccountSnapshotService : IAdminRecentAccountSnapshotService
{
    private const int MaxRecentAccounts = 5;
    private const int MaxConcurrentWarmups = 2;

    private readonly IAccountSnapshotCache _snapshotCache;
    private readonly IAccountSnapshotLoader _snapshotLoader;
    private readonly ILogger<AdminRecentAccountSnapshotService> _logger;
    private readonly object _sync = new();
    private readonly LinkedList<AccountSnapshotKey> _recentKeys = new();
    private readonly Dictionary<AccountSnapshotKey, Task> _warmups = new();
    private readonly SemaphoreSlim _warmupGate = new(MaxConcurrentWarmups, MaxConcurrentWarmups);
    private AccountSnapshotKey? _defaultKey;

    public AdminRecentAccountSnapshotService(
        IAccountSnapshotCache snapshotCache,
        IAccountSnapshotLoader snapshotLoader,
        ILogger<AdminRecentAccountSnapshotService> logger)
    {
        _snapshotCache = snapshotCache;
        _snapshotLoader = snapshotLoader;
        _logger = logger;
    }

    public void RecordSelection(Account account)
    {
        RecordAccount(account, isDefault: false);
    }

    public void WarmDefaultAccount(Account account)
    {
        RecordAccount(account, isDefault: true);
    }

    private void RecordAccount(Account account, bool isDefault)
    {
        if (account.AccountId <= 0 || string.IsNullOrWhiteSpace(account.Fund))
        {
            return;
        }

        var key = new AccountSnapshotKey(account.AccountId, account.Fund, 0).Normalize();
        lock (_sync)
        {
            if (isDefault)
            {
                _defaultKey = key;
                _recentKeys.Remove(key);
            }

            if (!isDefault && (!_defaultKey.HasValue || _defaultKey.Value != key))
            {
                var existing = _recentKeys.Find(key);
                if (existing is not null)
                {
                    _recentKeys.Remove(existing);
                }

                _recentKeys.AddFirst(key);
                while (_recentKeys.Count > MaxRecentAccounts)
                {
                    _recentKeys.RemoveLast();
                }
            }

            if (_warmups.ContainsKey(key))
            {
                return;
            }

            if (_snapshotCache.TryGet(key, out _))
            {
                // _logger.LogDebug("Admin recent account snapshot already cached: accountId={AccountId}, fund={Fund}", key.AccountId, key.Fund);
                return;
            }

            var warmup = WarmAsync(account, key);
            _warmups[key] = warmup;
            _ = ObserveWarmupAsync(key, warmup);
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _recentKeys.Clear();
            _defaultKey = null;
        }

        _snapshotCache.InvalidateAll();
    }

    private async Task WarmAsync(Account account, AccountSnapshotKey key)
    {
        await _warmupGate.WaitAsync();
        try
        {
            if (_snapshotCache.TryGet(key, out _))
            {
                return;
            }

            var contextAccount = new UserAccountContextAccount
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

            var queryRange = GetSnapshotQueryRange();
            await _snapshotCache.GetOrCreateAsync(
                key,
                cancellationToken => _snapshotLoader.LoadAsync(contextAccount, queryRange, key, cancellationToken),
                CancellationToken.None);

            // _logger.LogInformation(
            //     "Admin warmed account snapshot: accountId={AccountId}, fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}",
            //     key.AccountId,
            //     key.Fund,
            //     queryRange.StartDate,
            //     queryRange.EndDate);
        }
        finally
        {
            _warmupGate.Release();
        }
    }

    private async Task ObserveWarmupAsync(AccountSnapshotKey key, Task warmup)
    {
        try
        {
            await warmup;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin account snapshot warmup failed: accountId={AccountId}, fund={Fund}", key.AccountId, key.Fund);
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

    private static DateRange GetSnapshotQueryRange()
    {
        var now = DateTime.UtcNow;
        return new DateRange(
            new DateTime(now.Year - 2, 1, 1),
            new DateTime(now.Year, 12, 31));
    }
}
