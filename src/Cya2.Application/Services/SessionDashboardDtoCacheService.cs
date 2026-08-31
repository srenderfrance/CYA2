using Cya2.Application.DTOs;
using Cya2.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Cya2.Application.Services;

public class SessionDashboardDtoCacheService : ISessionDashboardDtoCacheService
{
    private const int MaxItemsPerUser = 15;
    private readonly ILogger<SessionDashboardDtoCacheService> _logger;

    private sealed class UserDashboardCache
    {
        public Dictionary<string, FinancialDashboardDto> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public LinkedList<string> LruFunds { get; } = new();
    }

    private readonly Dictionary<string, UserDashboardCache> _cacheByUser = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public SessionDashboardDtoCacheService(ILogger<SessionDashboardDtoCacheService> logger)
    {
        _logger = logger;
    }

    public bool TryGetDashboard(string userId, string fund, out FinancialDashboardDto dashboard)
    {
        dashboard = default!;
        var normalizedUserId = NormalizeKey(userId);
        var normalizedFund = NormalizeKey(fund);

        if (string.IsNullOrWhiteSpace(normalizedUserId) || string.IsNullOrWhiteSpace(normalizedFund))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(normalizedUserId, out var userCache))
            {
                _logger.LogInformation("Dashboard DTO cache miss: user={UserId}, fund={Fund}, reason=missing-user-cache", normalizedUserId, normalizedFund);
                return false;
            }

            if (!userCache.Items.TryGetValue(normalizedFund, out var dto))
            {
                _logger.LogInformation("Dashboard DTO cache miss: user={UserId}, fund={Fund}, reason=missing-fund, cachedFunds={CachedFunds}", normalizedUserId, normalizedFund, userCache.Items.Count);
                return false;
            }

            dashboard = dto;
            Touch(userCache, normalizedFund);
            _logger.LogInformation("Dashboard DTO cache hit: user={UserId}, fund={Fund}, accounts={Accounts}, cachedFunds={CachedFunds}", normalizedUserId, normalizedFund, dashboard.UserAccounts?.Count ?? 0, userCache.Items.Count);
            return true;
        }
    }

    public IReadOnlyCollection<string> GetFunds(string userId)
    {
        var normalizedUserId = NormalizeKey(userId);
        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return Array.Empty<string>();
        }

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(normalizedUserId, out var userCache))
            {
                return Array.Empty<string>();
            }

            return userCache.LruFunds.ToList();
        }
    }

    public void SetDashboard(string userId, string fund, FinancialDashboardDto dashboard, bool prioritize = false)
    {
        var normalizedUserId = NormalizeKey(userId);
        var normalizedFund = NormalizeKey(fund);
        if (string.IsNullOrWhiteSpace(normalizedUserId) || string.IsNullOrWhiteSpace(normalizedFund) || dashboard == null)
        {
            return;
        }

        lock (_sync)
        {
            if (!_cacheByUser.TryGetValue(normalizedUserId, out var userCache))
            {
                userCache = new UserDashboardCache();
                _cacheByUser[normalizedUserId] = userCache;
            }

            userCache.Items[normalizedFund] = dashboard;
            Touch(userCache, normalizedFund, prioritize);

            while (userCache.Items.Count > MaxItemsPerUser)
            {
                var evictFund = userCache.LruFunds.Last?.Value;
                if (string.IsNullOrWhiteSpace(evictFund))
                {
                    break;
                }

                userCache.LruFunds.RemoveLast();
                userCache.Items.Remove(evictFund);
            }

            _logger.LogInformation("Dashboard DTO cache set: user={UserId}, fund={Fund}, accounts={Accounts}, cachedFunds={CachedFunds}, prioritize={Prioritize}", normalizedUserId, normalizedFund, dashboard.UserAccounts?.Count ?? 0, userCache.Items.Count, prioritize);
        }
    }

    public void InvalidateAll()
    {
        lock (_sync) { _cacheByUser.Clear(); }
        _logger.LogInformation("Dashboard DTO cache invalidated (import/rollback)");
    }

    private static void Touch(UserDashboardCache userCache, string fund, bool prioritize = true)
    {
        var node = userCache.LruFunds.Find(fund);
        if (node != null)
        {
            userCache.LruFunds.Remove(node);
        }

        if (prioritize)
        {
            userCache.LruFunds.AddFirst(fund);
        }
        else
        {
            userCache.LruFunds.AddLast(fund);
        }
    }

    private static long EstimateBytes(FinancialDashboardDto dto)
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

    private static string NormalizeKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
    }
}
