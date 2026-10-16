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

    private readonly IDonationDataCache _donationDataCache;
    private readonly IDonationDataCacheLoader _donationDataCacheLoader;
    private readonly IAccountingDataCache _accountingDataCache;
    private readonly IAccountingDataCacheLoader _accountingDataCacheLoader;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<AdminRecentAccountSnapshotService> _logger;
    private readonly object _sync = new();
    private readonly LinkedList<string> _recentKeys = new();
    private readonly Dictionary<string, Task> _warmups = new();
    private readonly SemaphoreSlim _warmupGate = new(MaxConcurrentWarmups, MaxConcurrentWarmups);
    private string? _defaultKey;

    public AdminRecentAccountSnapshotService(
        IDonationDataCache donationDataCache,
        IDonationDataCacheLoader donationDataCacheLoader,
        IAccountingDataCache accountingDataCache,
        IAccountingDataCacheLoader accountingDataCacheLoader,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<AdminRecentAccountSnapshotService> logger)
    {
        _donationDataCache = donationDataCache;
        _donationDataCacheLoader = donationDataCacheLoader;
        _accountingDataCache = accountingDataCache;
        _accountingDataCacheLoader = accountingDataCacheLoader;
        _cacheInvalidationVersion = cacheInvalidationVersion;
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

        var key = GetKey(account);
        lock (_sync)
        {
            if (isDefault)
            {
                _defaultKey = key;
                _recentKeys.Remove(key);
            }

            if (!isDefault && (_defaultKey is null || _defaultKey != key))
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

            if (HasAuthoritativeCaches(account))
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

        _donationDataCache.InvalidateAll();
        _accountingDataCache.InvalidateAll();
    }

    private async Task WarmAsync(Account account, string key)
    {
        await _warmupGate.WaitAsync();
        try
        {
            if (HasAuthoritativeCaches(account))
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
            var donationKey = new DonationDataCacheKey(
                account.AccountId, _cacheInvalidationVersion.Donations,
                queryRange.StartDate, queryRange.EndDate).Normalize();
            await _donationDataCache.GetOrCreateAsync(
                donationKey,
                cancellationToken => _donationDataCacheLoader.LoadAsync(contextAccount, queryRange, donationKey, cancellationToken),
                CancellationToken.None);

            if (!Cya2.Core.Utilities.InternAccountUtility.IsInternFund(account.Fund))
            {
                var accountingKey = new AccountingDataCacheKey(
                    account.AccountId, account.AccountingClass ?? string.Empty, account.AccountNumber ?? string.Empty,
                    _cacheInvalidationVersion.Accounting,
                    queryRange.StartDate, queryRange.EndDate).Normalize();
                await _accountingDataCache.GetOrCreateAsync(
                    accountingKey,
                    cancellationToken => _accountingDataCacheLoader.LoadAsync(contextAccount, queryRange, accountingKey, cancellationToken),
                    CancellationToken.None);
            }

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

    private async Task ObserveWarmupAsync(string key, Task warmup)
    {
        try
        {
            await warmup;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin account cache warmup failed: key={Key}", key);
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
        => AccountFactCacheWindow.GetStandardWindow(DateTime.UtcNow);

    private static string GetKey(Account account)
        => $"{account.AccountId}:{account.Fund?.Trim()}";

    private bool HasAuthoritativeCaches(Account account)
    {
        var range = GetSnapshotQueryRange();
        var donationKey = new DonationDataCacheKey(
            account.AccountId, _cacheInvalidationVersion.Donations,
            range.StartDate, range.EndDate).Normalize();
        if (!_donationDataCache.TryGet(donationKey, out _)) return false;
        if (Cya2.Core.Utilities.InternAccountUtility.IsInternFund(account.Fund)) return true;
        var accountingKey = new AccountingDataCacheKey(
            account.AccountId, account.AccountingClass, account.AccountNumber,
            _cacheInvalidationVersion.Accounting, range.StartDate, range.EndDate).Normalize();
        return _accountingDataCache.TryGet(accountingKey, out _);
    }
}
