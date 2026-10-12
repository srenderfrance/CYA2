using System.Text.Json;
using Cya2.Application.Interfaces;
using Cya2.Application.Models;
using Cya2.Core.Entities;
using Cya2.Core.ReadModels;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class DashboardSessionCacheService : ISessionAccountDataCacheService
{
    private readonly IAccountSnapshotCache _accountSnapshotCache;
    private readonly IAccountSnapshotLoader _accountSnapshotLoader;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<DashboardSessionCacheService> _logger;

    private readonly Dictionary<string, DashboardAccountCacheData> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _cacheVersion;
    private string? _defaultFund;
    private string? _recentNonDefaultFund;

    public DashboardSessionCacheService(
        IAccountSnapshotCache accountSnapshotCache,
        IAccountSnapshotLoader accountSnapshotLoader,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<DashboardSessionCacheService> logger)
    {
        _accountSnapshotCache = accountSnapshotCache;
        _accountSnapshotLoader = accountSnapshotLoader;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger = logger;
    }

    public async Task<DashboardAccountCacheData> GetOrLoadAccountDataAsync(UserAccountContextAccount account, DateTime windowStart, DateTime windowEnd, bool isDefaultAccount)
    {
        var currentVersion = _cacheInvalidationVersion.Current;
        if (_cacheVersion != currentVersion)
        {
            _logger.LogInformation("Dashboard account-data cache reset: previousVersion={PreviousVersion}, currentVersion={CurrentVersion}", _cacheVersion, currentVersion);
            _cache.Clear();
            _defaultFund = null;
            _recentNonDefaultFund = null;
            _cacheVersion = currentVersion;
        }

        if (_cache.TryGetValue(account.Fund, out var existing)
            && existing.WindowStart <= windowStart
            && existing.WindowEnd >= windowEnd)
        {
            if (isDefaultAccount)
            {
                _defaultFund = account.Fund;
            }

            else
            {
                _recentNonDefaultFund = account.Fund;
            }

            _logger.LogInformation("Dashboard account-data cache hit: fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, isDefault={IsDefault}, accountsCached={AccountsCached}", account.Fund, windowStart, windowEnd, isDefaultAccount, _cache.Count);
            return existing;
        }

        _logger.LogInformation("Dashboard account-data cache miss: fund={Fund}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, isDefault={IsDefault}, accountsCached={AccountsCached}", account.Fund, windowStart, windowEnd, isDefaultAccount, _cache.Count);
        var loaded = await LoadAccountDataAsync(account, windowStart, windowEnd);
        _cache[account.Fund] = loaded;

        if (isDefaultAccount)
        {
            _defaultFund = account.Fund;
        }
        else
        {
            _recentNonDefaultFund = account.Fund;
        }

        TrimNonDefaultCaches();
        LogCacheStatus();

        return loaded;
    }

    public void LogCacheStatus()
    {
        var totalBytes = _cache.Values.Sum(v => v.ApproximateBytes);
        var totalAccounting = _cache.Values.Sum(v => v.AccountingData.Count);
        var totalDonations = _cache.Values.Sum(v => v.DonationData.Count);

        // _logger.LogInformation(
        //     "Dashboard session cache: AccountsCached={Accounts}, AccountingRows={AccountingRows}, DonationRows={DonationRows}, ApproxBytes={ApproxBytes}",
        //     _cache.Count,
        //     totalAccounting,
        //     totalDonations,
        //     totalBytes);
    }

    private async Task<DashboardAccountCacheData> LoadAccountDataAsync(UserAccountContextAccount account, DateTime windowStart, DateTime windowEnd)
    {
        var generation = _cacheInvalidationVersion.Current;
        var key = new AccountSnapshotKey(account.AccountId, account.Fund, generation).Normalize();
        var snapshot = await _accountSnapshotCache.GetOrCreateAsync(
            key,
            cancellationToken => _accountSnapshotLoader.LoadAsync(
                account,
                new Cya2.Core.ValueObjects.DateRange(windowStart, windowEnd),
                key,
                cancellationToken));

        var accounting = snapshot.Accounting
            .Select(record => new AccountingRecord
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
            })
            .ToList();

        var donations = snapshot.Donations
            .Select(record => new DonationRecord
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
            })
            .ToList();

        var subAccounts = snapshot.SubAccounts
            .Select(subAccount => new SubAccount
            {
                Id = subAccount.Id,
                AccountId = subAccount.AccountId,
                SubFund = subAccount.SubFund,
                Kind = subAccount.Kind
            })
            .ToList();

        var payload = new DashboardAccountCacheData
        {
            Fund = account.Fund,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            AccountingData = accounting,
            DonationData = donations,
            SubAccounts = subAccounts ?? new()
        };

        payload.ApproximateBytes = EstimateBytes(payload);

        _logger.LogInformation("Dashboard account-data cache loaded: fund={Fund}, accountingRows={AccountingRows}, donationRows={DonationRows}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, approxBytes={ApproxBytes}", payload.Fund, payload.AccountingData.Count, payload.DonationData.Count, payload.WindowStart, payload.WindowEnd, payload.ApproximateBytes);

        return payload;
    }

    private void TrimNonDefaultCaches()
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_defaultFund))
        {
            keep.Add(_defaultFund);
        }

        if (!string.IsNullOrWhiteSpace(_recentNonDefaultFund))
        {
            keep.Add(_recentNonDefaultFund);
        }

        var toRemove = _cache.Keys.Where(k => !keep.Contains(k)).ToList();
        foreach (var key in toRemove)
        {
            _cache.Remove(key);
        }
    }

    private static long EstimateBytes(DashboardAccountCacheData payload)
    {
        try
        {
            var accountingBytes = JsonSerializer.SerializeToUtf8Bytes(payload.AccountingData).LongLength;
            var donationBytes = JsonSerializer.SerializeToUtf8Bytes(payload.DonationData).LongLength;
            var subAccountBytes = JsonSerializer.SerializeToUtf8Bytes(payload.SubAccounts).LongLength;
            return accountingBytes + donationBytes + subAccountBytes;
        }
        catch
        {
            return 0;
        }
    }
}
