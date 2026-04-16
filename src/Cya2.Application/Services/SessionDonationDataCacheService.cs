using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Cya2.Application.Services;

public class SessionDonationDataCacheService : ISessionDonationDataCacheService
{
    private const int MaxItemsPerUser = 30; // keep a larger cache for donation data
    private readonly ILogger<SessionDonationDataCacheService> _logger;

    private sealed class UserDonationCache
    {
        public Dictionary<string, DonationDataDto> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public LinkedList<string> LruFunds { get; } = new();
    }

    private readonly Dictionary<string, UserDonationCache> _cacheByUser = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public SessionDonationDataCacheService(ILogger<SessionDonationDataCacheService> logger)
    {
        _logger = logger;
    }

    public bool TryGetDonationData(string userId, string fund, out DonationDataDto data)
    {
        var sw = Stopwatch.StartNew();
        data = default!;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fund)) return false;

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(userId, out var userCache))
            {
                _logger.LogInformation("Donation cache miss: user={UserId}, fund={Fund}, reason=missing-user-cache, elapsedMs={ElapsedMs}", userId, fund, sw.ElapsedMilliseconds);
                return false;
            }
            if (!userCache.Items.TryGetValue(fund, out var dto))
            {
                _logger.LogInformation("Donation cache miss: user={UserId}, fund={Fund}, reason=missing-fund, cachedFunds={CachedFunds}, elapsedMs={ElapsedMs}", userId, fund, userCache.Items.Count, sw.ElapsedMilliseconds);
                return false;
            }
            data = dto;
            Touch(userCache, fund);
            _logger.LogInformation(
                "Donation cache hit: user={UserId}, fund={Fund}, rows={Rows}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, approxBytes={ApproxBytes}, elapsedMs={ElapsedMs}",
                userId,
                fund,
                data.Donations?.Count ?? 0,
                data.CachedStartDate,
                data.CachedEndDate,
                EstimateBytes(data),
                sw.ElapsedMilliseconds);
            return true;
        }
    }

    public IReadOnlyCollection<string> GetFunds(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<string>();
        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(userId, out var userCache)) return Array.Empty<string>();
            return userCache.LruFunds.ToList();
        }
    }

    public void SetDonationData(string userId, string fund, DonationDataDto data, bool prioritize = false)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fund) || data == null) return;
        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(userId, out var userCache))
            {
                userCache = new UserDonationCache();
                _cacheByUser[userId] = userCache;
            }

            userCache.Items[fund] = data;
            Touch(userCache, fund, prioritize);

            while (userCache.Items.Count > MaxItemsPerUser)
            {
                var evictFund = userCache.LruFunds.Last?.Value;
                if (string.IsNullOrWhiteSpace(evictFund)) break;
                userCache.LruFunds.RemoveLast();
                userCache.Items.Remove(evictFund);
            }

            _logger.LogInformation(
                "Donation cache set: user={UserId}, fund={Fund}, rows={Rows}, range={Start:yyyy-MM-dd}..{End:yyyy-MM-dd}, approxBytes={ApproxBytes}, cachedFunds={CachedFunds}, prioritize={Prioritize}, elapsedMs={ElapsedMs}",
                userId,
                fund,
                data.Donations?.Count ?? 0,
                data.CachedStartDate,
                data.CachedEndDate,
                EstimateBytes(data),
                userCache.Items.Count,
                prioritize,
                sw.ElapsedMilliseconds);
        }
    }

    public void InvalidateAll()
    {
        lock (_sync) { _cacheByUser.Clear(); }
        _logger.LogInformation("Donation data cache invalidated (import/rollback)");
    }

    private static void Touch(UserDonationCache userCache, string fund, bool prioritize = true)
    {
        var node = userCache.LruFunds.Find(fund);
        if (node != null) userCache.LruFunds.Remove(node);
        if (prioritize) userCache.LruFunds.AddFirst(fund); else userCache.LruFunds.AddLast(fund);
    }

    private static long EstimateBytes(DonationDataDto dto)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(dto).LongLength;
        }
        catch
        {
            return 0;
        }
    }
}
