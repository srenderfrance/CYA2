using System.Text.Json;
using Cya2.Application.Interfaces;
using Cya2.Core.Interfaces;
using Cya2.Core.ReadModels;
using Microsoft.Extensions.Logging;

namespace Cya2.Application.Services;

public class DashboardSessionCacheService : ISessionAccountDataCacheService
{
    private readonly IExpenseReadRepository _expenseReadRepository;
    private readonly IDonationReadRepository _donationReadRepository;
    private readonly ICacheInvalidationVersion _cacheInvalidationVersion;
    private readonly ILogger<DashboardSessionCacheService> _logger;

    private readonly Dictionary<string, DashboardAccountCacheData> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _cacheVersion;
    private string? _defaultFund;
    private string? _recentNonDefaultFund;

    public DashboardSessionCacheService(
        IExpenseReadRepository expenseReadRepository,
        IDonationReadRepository donationReadRepository,
        ICacheInvalidationVersion cacheInvalidationVersion,
        ILogger<DashboardSessionCacheService> logger)
    {
        _expenseReadRepository = expenseReadRepository;
        _donationReadRepository = donationReadRepository;
        _cacheInvalidationVersion = cacheInvalidationVersion;
        _logger = logger;
    }

    public async Task<DashboardAccountCacheData> GetOrLoadAccountDataAsync(UserAccountContextAccount account, DateTime windowStart, DateTime windowEnd, bool isDefaultAccount)
    {
        var currentVersion = _cacheInvalidationVersion.Current;
        if (_cacheVersion != currentVersion)
        {
            _cache.Clear();
            _defaultFund = null;
            _recentNonDefaultFund = null;
            _cacheVersion = currentVersion;
        }

        if (_cache.TryGetValue(account.Fund, out var existing)
            && existing.WindowStart == windowStart
            && existing.WindowEnd == windowEnd)
        {
            if (isDefaultAccount)
            {
                _defaultFund = account.Fund;
            }

            else
            {
                _recentNonDefaultFund = account.Fund;
            }

            return existing;
        }

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

        _logger.LogInformation(
            "Dashboard session cache: AccountsCached={Accounts}, AccountingRows={AccountingRows}, DonationRows={DonationRows}, ApproxBytes={ApproxBytes}",
            _cache.Count,
            totalAccounting,
            totalDonations,
            totalBytes);
    }

    private async Task<DashboardAccountCacheData> LoadAccountDataAsync(UserAccountContextAccount account, DateTime windowStart, DateTime windowEnd)
    {
        var accounting = await _expenseReadRepository.GetAccountingDataByClassOrAccountNumberAndDateAsync(
            account.AccountingClass,
            account.AccountNumber,
            windowStart,
            windowEnd);

        var donations = await _donationReadRepository.GetDonationsByAccountAndDateRangeAsync(
            account.AccountId,
            account.Fund,
            windowStart,
            windowEnd);

        var payload = new DashboardAccountCacheData
        {
            Fund = account.Fund,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            AccountingData = accounting,
            DonationData = donations
        };

        payload.ApproximateBytes = EstimateBytes(payload);

        _logger.LogInformation(
            "Loaded dashboard cache for Fund={Fund}, AccountingRows={AccountingRows}, DonationRows={DonationRows}, Window={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, ApproxBytes={ApproxBytes}",
            payload.Fund,
            payload.AccountingData.Count,
            payload.DonationData.Count,
            payload.WindowStart,
            payload.WindowEnd,
            payload.ApproximateBytes);

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
            return accountingBytes + donationBytes;
        }
        catch
        {
            return 0;
        }
    }
}
